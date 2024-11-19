// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public abstract class McuBase : IDisposable, IMcu
    {
        protected enum ConfigCommandResult
        {
            NotSet = 0,
            Succeeded,
            CrcMismatch,
            Shutdown,
        }

        protected interface IMcuInflightItems : IDisposable
        {
            void Clear();
        }

        public readonly struct RegisterResponseHandlerDisposable : IDisposable
        {
            private readonly McuBase _mcu;
            private readonly McuCommand _response;
            private readonly McuResponseHandler _handler;

            public RegisterResponseHandlerDisposable(McuBase mcu, McuCommand response, McuResponseHandler handler)
            {
                _mcu = mcu;
                _response = response;
                _handler = handler;
            }

            public void Dispose()
                => _mcu.UnregisterResponseHandler(_response, _handler);
        }

        private readonly static TimeSpan _restartDelay = TimeSpan.FromSeconds(1);
        private readonly static TimeSpan _resetDelay = TimeSpan.FromSeconds(1);
        private const int _identifyBatchSize = 40;
        protected readonly ILoggerFactory _loggerFactory;
        protected readonly ILogger _logger;
        protected readonly IOptions<McuOptions> _options;
        protected readonly IAppDataWriter _appDataWriter;
        protected readonly McuManager _manager;
        protected readonly string _name;
        protected readonly IMcuClockSync _clockSync;
        protected readonly TimeSpan _responseTimeout;
        private volatile Exception? _currentError;
        private readonly PriorityScheduler _runScheduler;
        private readonly ConcurrentDictionary<McuBusKey, AsyncLock> _busLocks;
        private readonly TaskCompletionSource _lostCommunicationTaskSource;

        private readonly Lock _shutdownLock = new();
        private volatile string? _shutdownReason;
        private volatile bool _hasLostCommunication;
        protected volatile bool _isUpdatingFirmware;
        protected volatile bool _hasTriedToUpdateFirmware;

        private volatile bool _wasReady;
        private readonly Stopwatch _initialExceptionSupressionStopwatch;

        private volatile McuConfig _config = new McuConfig
        {
            IsDefault = true,
            Commands =
            {
                { "identify offset=%u count=%c", 1 },
            },
            Responses =
            {
                { "identify_response offset=%u data=%.*s", 0 },
            },
        };

        public McuManager Manager => _manager;
        public IMcuClockSync ClockSync => _clockSync;
        public McuConfig Config => _config;
        public AsyncEvent PreUpdatingEvent { get; } = new();
        public AsyncEvent BeforeClockSyncEvent { get; } = new();
        public AsyncEvent AfterReadyEvent { get; } = new();
        public string Name => _name;
        public bool IsShutdown => _shutdownReason != null;
        public bool HasLostCommunication => _hasLostCommunication;
        public bool IsUpdatingFirmware => _isUpdatingFirmware;
        public string? ShutdownReason => _shutdownReason;
        public abstract bool HasTimingCriticalCommandsScheduled { get; }
        public virtual Exception? CurrentError => _currentError;

        public McuBase(
            ILoggerFactory loggerFactory,
            ILogger logger,
            IAppDataWriter appDataWriter,
            McuManager manager,
            IOptions<McuOptions> options,
            IMcuClockSync clockSync)
        {
            var o = options.Value;
            _name = o.Name;
            _manager = manager;
            _options = options;
            _appDataWriter = appDataWriter;
            _responseTimeout = o.ResponseTimeout;
            _loggerFactory = loggerFactory;
            _logger = logger;
            _clockSync = clockSync;
            _config.Prepare(_name);
            _initialExceptionSupressionStopwatch = new();
            _busLocks = new();
            _lostCommunicationTaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

            // NOTE: use our own scheduler, so the high priority tasks wont compete with others
            _runScheduler = new PriorityScheduler(
                $"MCU {_name}, thread {{0}}",
                ThreadPriority.Highest,
                4 /* run, read, write, clocksync */ );

            _manager.ShutdownEvent.AddHandler(OnFirmwareShutdown);
        }

        private ValueTask OnMcuStats(Exception? exception, McuCommand? command, CancellationToken cancel)
        {
            if (command != null)
                _logger.LogTrace($"MCU {_name} stats: {command}");
            return ValueTask.CompletedTask;
        }

        private ValueTask OnFirmwareShutdown(McuShutdownMessage message, CancellationToken token)
        {
            if (!IsShutdown)
            {
                if (TryLookupCommand("emergency_stop", out var cmd))
                {
                    try
                    {
                        Send(cmd, McuCommandPriority.Shutdown, McuOccasion.Now);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to send shutdown command to MCU {_name}");
                    }
                }
            }
            return ValueTask.CompletedTask;
        }

        protected virtual ValueTask OnMcuShutdown(Exception? exception, McuCommand? command, CancellationToken cancel)
        {
            if (command != null)
            {
                string reason;
                lock (_shutdownLock)
                {
                    if (_shutdownReason != null)
                        return ValueTask.CompletedTask;
                    _shutdownReason = reason = command.TryGetArgumentString("static_string_id", _config) ?? "N/A";
                }
                _logger.LogError($"Got MCU {_name} shutdown. Reason: {_shutdownReason}. Shutting down manager.");
                PrinterGC.LogCollectionCount(_logger);
                _manager.Shutdown(new McuShutdownMessage { Mcu = this, Reason = reason });
            }
            return ValueTask.CompletedTask;
        }

        public AsyncLock GetLock(McuBusKey key)
        {
            if (!_busLocks.TryGetValue(key, out var locker))
            {
                lock (_busLocks)
                {
                    if (!_busLocks.TryGetValue(key, out locker))
                    {
                        locker = new();
                        _busLocks[key] = locker;
                    }
                }
            }
            return locker;
        }

        public bool TryLookupCommand(int commandId, [MaybeNullWhen(false)] out McuCommand command)
        {
            if (_config.IdToCommand.TryGetValue(commandId, out var found))
            {
                command = found.Clone();
                return true;
            }
            else
            {
                command = null!;
                return false;
            }
        }


        public bool TryLookupCommand(string commandFormat, [MaybeNullWhen(false)] out McuCommand command)
        {
            if (_config.StringToCommand.TryGetValue(commandFormat, out var found))
            {
                command = found.Clone();
                return true;
            }
            else
            {
                command = null!;
                return false;
            }
        }

        protected LogLevel GetLogErrorLevel()
            => !_wasReady && _initialExceptionSupressionStopwatch.Elapsed < _options.Value.InitialErrorSupressionDuration
                ? LogLevel.Information
                : LogLevel.Error;

        public Task Run(CancellationToken cancel)
            => Task.Factory.StartNew(() => RunInner(cancel),
                default,
                TaskCreationOptions.None,
                _runScheduler).Unwrap();

        protected void HandleLostCommunication(Exception? ex)
        {
            if (_wasReady && !_hasLostCommunication)
            {
                var message = $"Lost communication with MCU {_name}";
                _logger.LogError(ex, message);
                _hasLostCommunication = true;
                OnHasLostCommunication();

                // NOTE: following try/catch is to capture callstack
                try
                {
                    throw new McuException(message, ex);
                }
                catch (Exception ex2)
                {
                    _lostCommunicationTaskSource.TrySetException(ex2);
                }
            }
        }

        protected virtual void OnHasLostCommunication()
        {
        }

        private async Task RunInner(CancellationToken cancel)
        {
            try
            {
                var canTryRecover = true;

                _wasReady = false;
                _initialExceptionSupressionStopwatch.Restart();

                for (int attempt = 0; ; attempt++)
                {
                    cancel.ThrowIfCancellationRequested();
                    var inflightItems = CreateInflightItems();
                    try
                    {
                        using (var device = await CreateDevice(cancel))
                        {
                            _logger.LogInformation($"Created device for MCU {_name}, attempt = {attempt}");
                            var readWriteCancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                            var readWriteCancel = readWriteCancelSource.Token;
                            var encoder = new McuCodec();
                            Task readTask = Task.CompletedTask;
                            Task writeTask = Task.CompletedTask;
                            Task readWriteTask = Task.CompletedTask;
                            var shutdownSubs = new List<IDisposable>();
                            try
                            {
                                cancel.ThrowIfCancellationRequested();
                                var initSeq = await SynchronizeDevice(device, cancel);
                                (readTask, writeTask) = CreateReadWriteTask(device, initSeq, inflightItems, _runScheduler, readWriteCancel);

                                await IdentifyDevice(cancel);

                                if (canTryRecover)
                                {
                                    if (await CheckForShutdown(cancel))
                                    {
                                        throw new McuAutomatedRestartException(
                                            $"Automated MCU {_name} restart after {nameof(CheckForShutdown)}.{(_shutdownReason is not null and not "N/A" ? $" Reason = {_shutdownReason}" : "")}",
                                            reason: McuAutomatedRestartReason.Shutdown);
                                    }
                                }
                                if (!_hasTriedToUpdateFirmware)
                                    await CheckFirmwareUpdate(device, cancel);

                                shutdownSubs.Add(RegisterResponseHandler(null, this.LookupCommand("is_shutdown"), OnMcuShutdown));
                                shutdownSubs.Add(RegisterResponseHandler(null, this.LookupCommand("shutdown"), OnMcuShutdown));
                                RegisterResponseHandler(null, this.LookupCommand("stats"), OnMcuStats);
                                await BeforeClockSyncEvent.Invoke(cancel);
                                await _clockSync.Start(this, _runScheduler, cancel);

                                await RunAfterClockSync(async () =>
                                {
                                    var wasReady = await SendConfigCommands(cancel);

                                    // notify ready
                                    canTryRecover = false;
                                    _wasReady = wasReady;
                                    _currentError = null;
                                    _logger.LogInformation($"MCU {_name} is ready");
                                    await AfterReadyEvent.Invoke(cancel);

                                    try
                                    {
                                        using (_clockSync.UnreachableCancel.Register(() =>
                                        {
                                            if (!readWriteCancel.IsCancellationRequested)
                                                HandleLostCommunication(null);
                                        }))
                                        {
                                            // wait for errors
                                            var endedTask = await Task.WhenAny(readTask, writeTask, _lostCommunicationTaskSource.Task);
                                            await endedTask;
                                        }
                                    }
                                    catch (Exception ex) when (!readWriteCancel.IsCancellationRequested)
                                    {
                                        HandleLostCommunication(ex);
                                        throw;
                                    }
                                }, cancel);
                            }
                            catch (McuAutomatedRestartException ex)
                            {
                                _logger.LogDebug(ex, $"Attempting automated restart of MCU {_name}. Reason = {ex.Reason}");
                                _clockSync.Stop();
                                inflightItems.Clear(); // stop any retransmissions
                                foreach (var sub in shutdownSubs)
                                    sub.Dispose();
                                if (!await CheckForShutdown(cancel))
                                {
                                    Send(this.LookupCommand("emergency_stop"), McuCommandPriority.Shutdown, McuOccasion.Now);
                                    await Task.Delay(_restartDelay, cancel);
                                }
                                Send(this.LookupCommand("reset"), McuCommandPriority.Shutdown, McuOccasion.Now);
                                await Task.Delay(_resetDelay, cancel);
                                throw;
                            }
                            catch (Exception ex)
                            {
                                if (!cancel.IsCancellationRequested)
                                {
                                    _currentError = ex;
                                    _logger.Log(GetLogErrorLevel(), ex, $"Failed to run device for MCU {_name} (wasReady={_wasReady}): {ex.Message}");
                                }
                                // NOTE: do not rethrow, need to cancel and wait for all tasks below
                            }
                            finally
                            {
                                _clockSync.Stop();
                                readWriteCancelSource.Cancel();
                            }

                            // wait for everything to handle cancellation
                            await Task.WhenAll(readTask, writeTask);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!cancel.IsCancellationRequested)
                        {
                            _currentError = ex;
                            _logger.Log(GetLogErrorLevel(), ex, $"Exception while starting/running MCU {_name} (wasReady={_wasReady}): {ex.Message}");
                        }
                        if (_hasLostCommunication)
                            throw new McuException($"Lost communication with MCU {_name}", ex);
                        else if (_wasReady)
                            throw;
                    }
                    finally
                    {
                        await CancelResponseHandlers(new OperationCanceledException("MCU destroyed"), cancel);
                        inflightItems.Clear();
                        inflightItems.Dispose();
                        // NOTE: after inflight (frees buffers)
                        ClearCommandQueue();
                    }

                    // final checks, if we have got here without exception
                    cancel.ThrowIfCancellationRequested();
                    if (_hasLostCommunication) 
                        throw new McuException($"Lost communication with MCU {_name}");
                    else if (_wasReady)
                        throw new McuException($"MCU {_name} was previously ready and cannot be restarted");
                    await Task.Delay(_restartDelay, cancel);
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    var msg = $"Unhandled exception in main thread for MCU {_name}, shutting down";
                    _logger.LogError(ex, msg);
                    _manager.Shutdown(new McuShutdownMessage
                    {
                        Mcu = this,
                        Reason = msg,
                        Exception = ex,
                    });
                }
            }
        }

        protected virtual Task RunAfterClockSync(Func<Task> func, CancellationToken cancel)
            => func();

        protected abstract IMcuInflightItems CreateInflightItems();

        protected abstract (Task ReadTask, Task WriteTask) CreateReadWriteTask(
            IDisposable device,
            ulong initSeq,
            IDisposable inflightItems,
            PriorityScheduler runScheduler,
            CancellationToken cancel);
        protected abstract void ClearCommandQueue();

        protected abstract Task CheckFirmwareUpdate(IDisposable device, CancellationToken cancel);

        protected abstract ValueTask CancelResponseHandlers(Exception responseException, CancellationToken cancel);

        public abstract IDisposable RegisterResponseHandler(McuCommand? request, McuCommand response, McuResponseHandler handler);

        public IDisposable RegisterResponseHandler(McuCommand? request, McuCommand response, Func<McuCommand, bool>? responseFilter, out Task<McuCommand> task, CancellationToken cancel)
        {
            var taskSource = new TaskCompletionSource<McuCommand>(TaskCreationOptions.RunContinuationsAsynchronously);
            CancellationTokenRegistration cancelSubscription = default;
            cancelSubscription = cancel.Register(() =>
            {
                cancelSubscription.Dispose();
                taskSource.TrySetCanceled(cancel);
            });
            task = taskSource.Task;
            return RegisterResponseHandler(request, response, (ex, response, cancel) =>
            {
                cancelSubscription.Dispose();
                if (response != null)
                {
                    if (responseFilter == null || responseFilter(response))
                        taskSource.TrySetResult(response);
                }
                else if (ex != null)
                    taskSource.TrySetException(ex);
                else
                    throw new InvalidOperationException("Expected response or exception");
                return ValueTask.CompletedTask;
            });
        }

        protected abstract void UnregisterResponseHandler(McuCommand response, McuResponseHandler handler);

        public abstract void SendCancel(McuSendResult sendId);

        public abstract void SendCancel(IEnumerable<McuSendResult> sendIds);

        public abstract Task SendWait(McuCommand command, int priority, McuOccasion clock, CancellationToken cancel = default);

        public abstract McuSendResult Send(McuCommand command, int priority, McuOccasion clock, McuSendResult? cancelFirst = default);

        public abstract bool TryReplace(McuSendResult id, McuCommand command);

        public async Task<McuCommand> SendWithResponse(McuCommand request, McuCommand response, Func<McuCommand, bool>? responseFilter, int priority, McuOccasion clock, TimeSpan? timeout = default, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            using (RegisterResponseHandler(request, response, responseFilter, out var task, cancel))
            {
                Send(request, priority, clock);
                if (timeout != TimeSpan.MaxValue && timeout != Timeout.InfiniteTimeSpan)
                    return await task.WaitAsync(timeout ?? _responseTimeout, cancel);
                else
                    return await task;
            }
        }

        private async Task<bool> CheckForShutdown(CancellationToken cancel)
        {
            var priority = McuCommandPriority.Initialize;
            var cmdGetConfig = this.LookupCommand("get_config");
            if (!TryLookupCommand("config is_config=%c crc=%u move_count=%hu is_shutdown=%c static_string_id=%hu", out var cmdGetConfigResponse))
                cmdGetConfigResponse = this.LookupCommand("config is_config=%c crc=%u move_count=%hu is_shutdown=%c");
            var response = await SendWithResponse(
                cmdGetConfig,
                cmdGetConfigResponse,
                null,
                priority,
                McuOccasion.Now,
                cancel: cancel);

            if (response["is_shutdown"].Boolean)
            {
                var reason = response.TryGetArgumentString("static_string_id", _config) ?? "N/A";
                return true;
            }
            return false;
        }

        protected abstract Task<bool> SendConfigCommands(CancellationToken cancel);

        protected async Task SendConfigCommands(McuConfigCommands commands, CancellationToken cancel)
        {
            var result = await TrySendConfigCommands(commands, cancel);
            if (result != ConfigCommandResult.Succeeded)
                throw new McuAutomatedRestartException(
                    $"Failed to initialize MCU {_name}, result = {result}.{(ShutdownReason is not null and not "N/A" ? $" Reason = {ShutdownReason}" : "")}",
                    reason: result switch { 
                        ConfigCommandResult.CrcMismatch => McuAutomatedRestartReason.CrcMismatch,
                        _ => McuAutomatedRestartReason.Shutdown,
                    });
        }

        protected async Task<ConfigCommandResult> TrySendConfigCommands(McuConfigCommands commands, CancellationToken cancel)
        {
            var options = _options.Value;
            var priority = McuCommandPriority.Initialize;
            var cmdGetConfig = this.LookupCommand("get_config");
            if (!TryLookupCommand("config is_config=%c crc=%u move_count=%hu is_shutdown=%c static_string_id=%hu", out var cmdGetConfigResponse))
                cmdGetConfigResponse = this.LookupCommand("config is_config=%c crc=%u move_count=%hu is_shutdown=%c");
            var response = await SendWithResponse(
                cmdGetConfig,
                cmdGetConfigResponse,
                null,
                priority,
                McuOccasion.Now,
                cancel: cancel);

            bool HandleIsShutdown()
            {
                if (response["is_shutdown"].Boolean)
                {
                    TrySetShutdownReason(response.TryGetArgumentString("static_string_id", Config) ?? "N/A");
                    return true;
                }
                return false;
            }

            if (HandleIsShutdown())
                return ConfigCommandResult.Shutdown;
            int? prevCrc = response["is_config"].Boolean ? response["crc"].Int32 : null;
            var requestedMoveCount = _options.Value.RequestedMoveCount;
            if (!await commands.TryInitializeAndSend(_logger, this, priority, prevCrc, requestedMoveCount, cancel))
                return ConfigCommandResult.CrcMismatch;
            response = await SendWithResponse(
                cmdGetConfig,
                cmdGetConfigResponse,
                null,
                priority,
                McuOccasion.Now,
                cancel: cancel);
            HandleIsShutdown();
            if (!response["is_config"].Boolean || response["is_shutdown"].Boolean)
                return ConfigCommandResult.Shutdown;
            var availableMoveCount = response["move_count"].Int32;
            if (requestedMoveCount > availableMoveCount)
                _logger.LogWarning($"Got less available moves than requested. MCU {_name}, {availableMoveCount} moves available, {requestedMoveCount ?? 0} requested");
            else
                _logger.LogInformation($"Initialized MCU {_name}, {availableMoveCount} moves available, {requestedMoveCount ?? 0} requested");
            return ConfigCommandResult.Succeeded;
        }

        private async Task IdentifyDevice(CancellationToken cancel)
        {
            var identifyCmd = this.LookupCommand("identify offset=%u count=%c");
            var identifyResponse = this.LookupCommand("identify_response offset=%u data=%.*s");
            var offset = 0;
            var ms = new MemoryStream();
            var batchCount = 0;
            while (true)
            {
                batchCount++;
                identifyCmd[0] = offset;
                identifyCmd[1] = _identifyBatchSize;
                var response = await SendWithResponse(
                    identifyCmd,
                    identifyResponse,
                    null,
                    McuCommandPriority.Identify,
                    McuOccasion.Now,
                    cancel: cancel);
                var offset2 = response[0].Int32;
                if (offset2 == offset) // may be out of order due to retransmit
                {
                    var buffer = response[1].Buffer;
                    ms.Write(buffer.AsSpan());
                    offset += buffer.Count;
                    if (buffer.Count != _identifyBatchSize)
                        break;
                }
            }
            ms.Position = 2; // ZLIB header
            using (var gzip = new DeflateStream(ms, CompressionMode.Decompress, true))
            {
                _config = McuConfig.Parse(_name, gzip);
            }
            _logger.LogInformation($"Got identify config from MCU {_name} in {batchCount} batches");
        }

        protected abstract ValueTask<ulong> SynchronizeDevice(IDisposable device, CancellationToken cancel);

        protected abstract ValueTask<IDisposable> CreateDevice(CancellationToken cancel);

        private async Task ClearShutdown(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            Send(this.LookupCommand("clear_shutdown"), McuCommandPriority.Shutdown, McuOccasion.Now);
            await Task.Delay(_restartDelay);
        }

        public virtual void Dispose()
        {
            _manager.ShutdownEvent.RemoveHandler(OnFirmwareShutdown);
            _runScheduler.Dispose();
        }

        public override string ToString()
            => _name;

        public abstract void RegisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler);
        public abstract void UnregisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler);

        protected void TrySetShutdownReason(string reason)
        {
            lock (_shutdownLock)
            {
                if (_shutdownReason == null)
                    _shutdownReason = reason;
            }
        }
    }
}
