// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Movement;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.Printer
{
    public class McuPrinterClientOptions : PrinterClientBaseOptions
    {
        public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan RunnerRestartDelay { get; set; } = TimeSpan.FromSeconds(1);
        public bool EnableRunner { get; set; } = false;
    }

    public sealed class McuPrinterClient : PrinterClientBase, IAsyncDisposable
    {
        private readonly ILogger<McuPrinterClient> _logger;
        private readonly IOptionsMonitor<McuPrinterClientOptions> _options;
        private readonly IPrinterFirmwareRunner[] _firmwareRunners;
        private readonly McuManager _manager;
        private readonly IEnumerable<IObjectFactory<IPrinterClientInitializer, object>> _initializers;
        private volatile TaskCompletionSource<McuManager> _connectedManagerSource;
        private Task _runTask;
        private CancellationTokenSource _cancelSource;
        private long _connectionIndex;

        private readonly AsyncLock _runnerLock = new();
        private Task?[] _firmwareRunnerTasks;
        private CancellationTokenSource?[] _firmwareRunnerCancelSources;

        public override bool IsShutdown => _manager.IsShutdown == true;
        public override string? ShutdownReason => _manager.ShutdownReason?.ToString();
        public override bool HasLostCommunication => _manager.HasLostCommunication == true;

        public McuManager? ManagerIfReady
        {
            get
            {
                var source = _connectedManagerSource;
                if (source.Task.IsCompleted)
                    return source.Task.GetAwaiter().GetResult();
                else
                    return null;
            }
        }

        public McuManager Manager
        {
            get
            {
                var manager = ManagerIfReady;
                if (manager == null)
                    throw new McuException("Printer is not yet ready");
                var shutdownReason = manager.ShutdownReason;
                if (shutdownReason != null)
                    throw new PrinterFirmwareShutdownException($"Firmware is shutdown: {shutdownReason}");
                return manager;
            }
        }

        public McuManager ManagerEvenInShutdown => _manager;

        public AsyncEvent<McuManager> ManagerSetEvent { get; } = new();

        public override bool ShouldSendSafeCheckpoints => false;

        public override bool IsConnected => _connectedManagerSource != null && _connectedManagerSource.Task.IsCompleted;

        public override long ConnectionIndex => Interlocked.Read(ref _connectionIndex);

        public McuPrinterClient(
            ILogger<McuPrinterClient> logger,
            IOptionsMonitor<McuPrinterClientOptions> options,
            IEnumerable<IPrinterFirmwareRunner> firmwareRunners,
            McuManager manager,
            IObjectFactory<McuMovementClient, object> movementFactory,
            IEnumerable<IObjectFactory<IPrinterClientInitializer, object>> initializers)
            : base(logger, options, movementFactory)
        {
            _logger = logger;
            _options = options;
            _firmwareRunners = firmwareRunners.ToArray();
            _firmwareRunnerTasks = new Task?[_firmwareRunners.Length];
            _firmwareRunnerCancelSources = new CancellationTokenSource?[_firmwareRunners.Length];
            _manager = manager;
            _initializers = initializers;
            _connectedManagerSource = new TaskCompletionSource<McuManager>(TaskCreationOptions.RunContinuationsAsynchronously);
            _cancelSource = new CancellationTokenSource();
            _runTask = Task.Run(() => RunTask(_cancelSource.Token));
        }

        private async Task EnsureRunnerRunningAndUpToDate(bool forceRestart, CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            if (!options.EnableRunner)
                return;
            _logger.LogInformation($"Ensuring MCU runners are started, since {nameof(options.EnableRunner)} is true");
            using (await _runnerLock.LockAsync(cancel))
            {
                for (int i_ = 0; i_ < _firmwareRunners.Length; i_++)
                {
                    var i = i_;
                    var runner = _firmwareRunners[i];
                    await runner.UpdateConfiguration();
                    var task = _firmwareRunnerTasks[i];
                    if (task?.IsCompleted == false && !forceRestart)
                        return;

                    _firmwareRunnerCancelSources[i]?.Cancel();
                    if (task != null)
                        await task;

                    var cancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                    var runnerCancel = cancelSource.Token;
                    _firmwareRunnerCancelSources[i] = cancelSource;
                    _firmwareRunnerTasks[i] = Task.Run(async () =>
                    {
                        while (true)
                        {
                            try
                            {
                                await runner.Run(runnerCancel);
                                throw new McuException($"MCU runner {runner}[{i}] has exited, did the MCU crash?");
                            }
                            catch (Exception ex)
                            {
                                if (runnerCancel.IsCancellationRequested)
                                {
                                    _logger.LogInformation($"MCU runner {runner}[{i}] has been cancelled");
                                    break;
                                }
                                _logger.LogError(ex, $"MCU runner {runner}[{i}] has thrown an exception");
                            }
                            await Task.Delay(options.RunnerRestartDelay, runnerCancel);
                        }
                    });
                }
            }
        }

        private async Task InitializeManager(McuManager manager, CancellationToken cancel)
        {
            var context = new McuInitializeCommandContext(manager);

            foreach (var initializer in _initializers)
                await initializer.CreateAndCall(x => x.InitializeClient(this, context, cancel));
        }

        private async Task RunTask(CancellationToken cancel)
        {
            try
            {
                foreach (var initializer in _initializers)
                    await initializer.CreateAndCall(x => x.InitializePrinter(cancel));

                var options = _options.CurrentValue;
                var runnerCancelSource = new CancellationTokenSource(); // NOTE: no link, see below!
                var runnerCancel = runnerCancelSource.Token;
                var managerCancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
                var managerCancel = managerCancelSource.Token;
                try
                {
                    await EnsureRunnerRunningAndUpToDate(false, runnerCancel); // NOTE: exit runner only AFTER manager exited. Exiting runner earlier that that may induce exceptions due to stopped MCUs.

                    var runTask = Task.CompletedTask;
                    try
                    {
                        _manager.ShutdownEvent.AddHandler(OnFirmwareShutdown);
                        runTask = _manager.Run(managerCancel, managerCancel);
                        _ = _manager.HasStartedTask.ContinueWith(async _ =>
                        {
                            _logger.LogInformation($"Running MCU manager initializers");
                            await InitializeManager(_manager, managerCancel);

                            _logger.LogInformation($"Running MCU manager assigned");
                            Interlocked.Increment(ref _connectionIndex);
                            _connectedManagerSource.TrySetResult(_manager);
                            await ConnectedEvent.Invoke(managerCancel);
                            await ManagerSetEvent.Invoke(_manager, managerCancel);
                        });
                        await runTask;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception while creating/running MCU manager");
                    }
                    finally
                    {
                        runnerCancelSource.Cancel();
                        managerCancelSource.Cancel();
                        try
                        {
                            await runTask;
                        }
                        catch
                        {
                            // swallow, everything is cannceled anyway
                        }
                        _connectedManagerSource = new TaskCompletionSource<McuManager>(TaskCreationOptions.RunContinuationsAsynchronously);
                    }
                }
                catch (Exception ex) when (!cancel.IsCancellationRequested)
                {
                    if (!cancel.IsCancellationRequested && !managerCancel.IsCancellationRequested)
                    {
                        _logger.LogError(ex, "Exception while creating/running MCU manager");
                        _manager.Shutdown(new McuShutdownMessage
                        {
                            Reason = "Exception while creating/running MCU manager",
                            Exception = ex,
                            Mcu = null,
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    _logger.LogCritical(ex, $"Unhandled exception while running managers");
            }
        }

        private ValueTask OnFirmwareShutdown(McuShutdownMessage message, CancellationToken cancel)
            => FirmwareShutdownEvent.Invoke(message.Mcu == null ? message.Reason : $"{message.Reason} (MCU {message.Mcu})", cancel);

        public override Task Restart(PrinterClientRestartFlags type, CancellationToken cancel = default)
        {
            // NOTE: is somebody decided to implement firmware restart here, it should be possible
            //       but it is very neccessary to make sure there are no leaks and issues left behind.
            //       Current design is based on that once MCU shutdown happens, only application restart can fix it.
            //       There are shutdown handlers registered, states initialized, these would all need to be reset if shutdown is cleared.
            throw new NotSupportedException($"Firmware restart is not supported by {GetType()}. Restart the application/printer instead.");
        }

        public override async Task WaitForConnection(CancellationToken cancel = default)
        {
            await _connectedManagerSource.Task.WaitAsync(cancel);
        }

        public async ValueTask DisposeAsync()
        {
            _cancelSource.Cancel();
            await _runTask;
        }

        public override void Shutdown(string reason, Exception? ex, IPrinterClientCommandContext? context = null)
        {
            var manager = McuInitializeCommandContext.GetManager(this, context);
            manager.Shutdown(new McuShutdownMessage
            {
                Mcu = null,
                Exception = ex,
                Reason = reason,
            });
        }

        public override (string Key, string Message)[] GetConnectionStatus()
            => _manager.GetConnectionStatus();
    }
}
