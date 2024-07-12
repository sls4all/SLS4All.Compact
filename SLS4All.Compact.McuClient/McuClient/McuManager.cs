// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.McuClient.Sensors;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public abstract class McuManager
    {
        public readonly struct LockMasterQueueDisposable : IDisposable
        {
            public McuManager Manager { get; }

            public McuTimestamp this[object key]
            {
                get
                {
                    if (Manager._masterTimestamp.TryGetValue(key, out var res))
                        return res;
                    else
                        return default;
                }
                set => Manager._masterTimestamp[key] = value;
            }

            public LockMasterQueueDisposable(McuManager manager)
            {
                Manager = manager;
            }

            public void Dispose()
            {
                Manager.QueueMasterEnd();
            }
        }

        protected sealed class McuItem : IDisposable
        {
            private volatile IMcu? _mcuLazy;

            public required IObjectFactory<IMcu, object> McuFactory { get; init; }
            public required string Name { get; init; }
            public IMcu Mcu
            {
                get
                {
                    var mcu = _mcuLazy;
                    if (mcu == null)
                        _mcuLazy = mcu = McuFactory.CreateObject();
                    return mcu;
                }
            }
            public Task? RunTask { get; set; }

            public void Dispose()
            {
                var mcu = _mcuLazy;
                if (mcu != null)
                    McuFactory.DestroyObject(mcu);
            }

            public override string ToString()
                => Name;
        }

        private readonly ILoggerFactory _loggerFactory;
        protected readonly ILogger _logger;
        protected readonly IOptionsMonitor<McuManagerOptions> _options;
        protected readonly IAppDataWriter _appDataWriter;
        protected readonly IEnumerable<IMcuDeviceFactory> _deviceFactories;
        protected readonly FrozenDictionary<string, McuItem> _mcuItems;
        private readonly CancellationTokenSource _runningCancelSource;
        private volatile McuShutdownMessage? _managerShutdownReason;
        private readonly List<(IMcu? Mcu, Delegate Delegate)> _setupHandlers;
        private readonly TaskCompletionSource _hasStartedSource;
        private readonly object _queueMasterLock = new object();
        private readonly Dictionary<object, McuTimestamp> _masterTimestamp;

        public AsyncEvent<McuShutdownMessage> ShutdownEvent { get; } = new();
        public CancellationToken RunningCancel => _runningCancelSource.Token;
        public Task HasStartedTask => _hasStartedSource.Task;
        public McuManagerShutdownReason? ShutdownReason
        {
            get
            {
                var managerShutdownReason = _managerShutdownReason;
                if (managerShutdownReason != null)
                {
                    if (managerShutdownReason != null)
                        return new McuManagerShutdownReason(managerShutdownReason.Mcu, managerShutdownReason.Reason);
                }
                foreach (var mcu in _mcuItems.Values)
                {
                    var mcuShutdownReason = mcu.Mcu.ShutdownReason;
                    if (mcuShutdownReason != null)
                        return new McuManagerShutdownReason(mcu.Mcu, mcuShutdownReason);
                }
                return null;
            }
        }
        public bool IsShutdown => ShutdownReason != null;
        public bool HasTimingCriticalCommandsScheduled
        {
            get
            {
                foreach (var mcu in _mcuItems.Values)
                    if (mcu.Mcu.HasTimingCriticalCommandsScheduled)
                        return true;
                return false;
            }
        }
        public bool HasLostCommunication
        {
            get
            {
                foreach (var mcu in _mcuItems.Values)
                    if (mcu.Mcu.HasLostCommunication)
                        return true;
                return false;
            }
        }
        public IEnumerable<IMcu> Mcus => _mcuItems.Values.Select(x => x.Mcu);

        public abstract IMcuPowerManager PowerManager { get; }
        public abstract FrozenDictionary<string, IMcuStepper> Steppers { get; }
        public abstract FrozenDictionary<string, IMcuOutputPin> OutputPins { get; }
        public abstract FrozenDictionary<string, IMcuButton> Buttons { get; }
        public abstract FrozenDictionary<string, IMcuTemperatureSensor> TemperatureSensors { get; }
        public abstract FrozenDictionary<string, IMcuHeater> Heaters { get; }
        public abstract FrozenDictionary<string, IMcuSdCard> SdCards { get; }
        public bool IsHeating
        {
            get
            {
                foreach (var heater in Heaters.Values)
                    if (heater.Target != null)
                        return true;
                return false;
            }
        }
        public abstract TimeSpan StepperQueueHigh { get; }
        public abstract TimeSpan StepperQueueLow { get; }

        protected abstract bool HasKeepAliveEnablePinsEnabled { get; }
        public int CreatedArenas
        {
            get
            {
                var count = 0;
                foreach (var mcu in _mcuItems.Values)
                    count += (mcu.Mcu as McuAbstract)?.CommandArena.CreatedArenas ?? 0;
                return count;
            }
        }

        public long CreatedCommands
        {
            get
            {
                var count = 0L;
                foreach (var mcu in _mcuItems.Values)
                    count += (mcu.Mcu as McuAbstract)?.CreatedCommands ?? 0;
                return count;
            }
        }

        public McuManager(
            ILoggerFactory loggerFactory,
            ILogger logger,
            IOptionsMonitor<McuManagerOptions> options,
            IAppDataWriter appDataWriter,
            IEnumerable<IMcuDeviceFactory> deviceFactories)
        {
            _loggerFactory = loggerFactory;
            _logger = logger;
            _options = options;
            _appDataWriter = appDataWriter;
            _masterTimestamp = new Dictionary<object, McuTimestamp>();

            _hasStartedSource = new();
            _setupHandlers = new();
            _runningCancelSource = new();
            _deviceFactories = deviceFactories;
            _mcuItems = CreateMcus().ToFrozenDictionary();
        }

        public bool TryGetMcu(string name, [MaybeNullWhen(false)] out IMcu mcu)
        {
            if (_mcuItems.TryGetValue(name, out var value))
            {
                mcu = value.Mcu;
                return true;
            }
            else
            {
                mcu = default;
                return false;
            }
        }

        private Dictionary<string, McuItem> CreateMcus()
        {
            var options = _options.CurrentValue;
            var managerOptions = _options.CurrentValue;
            var mcuItems = new Dictionary<string, McuItem>();
            foreach (var mcuOptions_ in managerOptions.Mcus.GetOrderedEnabledValues())
            {
                var mcuOptions = mcuOptions_;
                var mcuFactory = new DelegatedObjectFactory<IMcu, object>(_ =>
                {
                    var clockSync = CreateClockSync(_loggerFactory);
                    var mcu = CreateMcu(_loggerFactory, _appDataWriter, this, Options.Create(mcuOptions), clockSync, _deviceFactories);
                    return mcu;
                }, mcu =>
                {
                    var clockSync = mcu.ClockSync;
                    (mcu as IDisposable)?.Dispose();
                    (clockSync as IDisposable)?.Dispose();
                });
                var item = new McuItem
                {
                    Name = mcuOptions.Name,
                    McuFactory = mcuFactory,
                };
                mcuItems.Add(mcuOptions.Name, item);
            }
            return mcuItems;
        }

        protected abstract IMcuClockSync CreateClockSync(
            ILoggerFactory loggerFactory);

        protected abstract IMcu CreateMcu(
            ILoggerFactory loggerFactory, 
            IAppDataWriter appDataWriter, 
            McuManager mcuManagerBase, 
            IOptions<McuManagerOptions.ManagerMcuOptions> options, 
            IMcuClockSync clockSync, 
            IEnumerable<IMcuDeviceFactory> deviceFactories);

        public async Task Run(CancellationToken runCancel, CancellationToken initializeCancel)
        {
            var options = _options.CurrentValue;
            var cancel = _runningCancelSource.Token;
            try
            {
                // start MCUs
                var mcus = _mcuItems.Values.ToArray();
                _logger.LogInformation($"Will start with {mcus.Length} MCUs: {string.Join(", ", mcus.AsEnumerable())}");

                var enablePins = HasKeepAliveEnablePinsEnabled ? options.KeepaliveEnablePins.GetOrderedEnabledValues().Select(x => OutputPins[x]).ToArray() : [];
                var mcusReady = Enumerable.Range(0, mcus.Length).Select(x => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).ToArray();

                using (initializeCancel.Register(_runningCancelSource.Cancel))
                using (runCancel.Register(_runningCancelSource.Cancel))
                {
                    foreach (var pin in enablePins)
                        pin.SetupMaxDuration(options.DisableKeepaliveEnable ? TimeSpan.Zero : options.KeepaliveEnableMaxDuration);

                    for (int i = 0; i < mcus.Length; i++)
                    {
                        var mcu = mcus[i];
                        var mcuReady = mcusReady[i];
                        mcu.Mcu.AfterReadyEvent.AddHandler(cancel =>
                        {
                            mcuReady.TrySetResult();
                            return Task.CompletedTask;
                        });
                        mcu.RunTask = Task.Factory.StartNew(async () =>
                        {
                            cancel.ThrowIfCancellationRequested();
                            try
                            {
                                _logger.LogInformation($"MCU {mcu} is starting Run()");
                                await mcu.Mcu.Run(cancel);
                            }
                            finally
                            {
                                _logger.LogInformation($"MCU {mcu} has ended Run()");
                            }
                        },
                            default, TaskCreationOptions.None, TaskScheduler.Current)
                            .Unwrap();
                    }

                    // wait for all MCUs
                    await Task.WhenAll(mcusReady.Select(x => x.Task)).WaitAsync(cancel);

                    // fire setup
                    _logger.LogInformation($"Calling setup handlers");
                    foreach (var item in _setupHandlers)
                    {
                        try
                        {
                            _logger.LogDebug($"Calling setup handler for MCU '{item.Mcu}' on target {item.Delegate.Target}");
                            if (item.Delegate is Func<CancellationToken, ValueTask> valueTask)
                                await valueTask(cancel);
                            else
                                await ((Func<CancellationToken, Task>)item.Delegate)(cancel);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError($"Unhandled exception while calling setup handler for MCU '{item.Mcu}' on target {item.Delegate.Target}");
                            Shutdown(new McuShutdownMessage
                            {
                                Reason = "Exception while running MCU manager",
                                Exception = ex,
                                Mcu = item.Mcu,
                            });
                            throw;
                        }
                    }
                }

                // done!
                using (runCancel.Register(_runningCancelSource.Cancel))
                {
                    _logger.LogInformation("Running!");
                    _hasStartedSource.TrySetResult();

                    // let MCUs run, update keepalive pins
                    var allRunCancelSource = new CancellationTokenSource();
                    try
                    {
                        if (enablePins.Length > 0)
                            _ = RunEnablePins(enablePins, allRunCancelSource.Token);
                        await Task.WhenAll(mcus.Select(x => x.RunTask!));
                    }
                    finally
                    {
                        _logger.LogInformation("All MCUs finished running");
                        allRunCancelSource.Cancel();
                    }
                }
            }
            catch (Exception ex)
            {
                _hasStartedSource.TrySetException(ex);
                if (!cancel.IsCancellationRequested)
                {
                    _logger.LogError(ex, $"Unhandled exception in manager thread");
                    Shutdown(new McuShutdownMessage
                    {
                        Reason = "Exception while running MCU manager",
                        Exception = ex,
                        Mcu = null,
                    });
                }
            }
            finally
            {
                _runningCancelSource.Cancel();
                foreach (var item in _mcuItems.Values)
                    item.Dispose();
            }
        }

        protected virtual async Task RunEnablePins(IMcuOutputPin[] enablePins, CancellationToken cancel)
        {
            try
            {
                using (var enablePinsThreadPool = new PriorityScheduler("EnablePinsThread[{0}]", ThreadPriority.Normal, 1))
                {
                    await Task.Factory.StartNew(async () =>
                    {
                        var options = _options.CurrentValue;
                        var timer = new PeriodicTimer(options.KeepaliveEnablePeriod);
                        for (long i = 0; ; i++)
                        {
                            foreach (var pin in enablePins)
                            {
                                if (pin.CurrentValue.IsNonZero || i == 0) // NOTE: do not overwrite user disabled values
                                {
                                    try
                                    {
                                        pin.SetImmediate(true, McuCommandPriority.Default);
                                    }
                                    catch (Exception ex)
                                    {
                                        _logger.LogDebug(ex, $"Failed to set pin {pin}");
                                    }
                                }
                            }
                            await timer.WaitForNextTickAsync(cancel).AsTask();
                        }
                    }, cancel, TaskCreationOptions.None, enablePinsThreadPool).Unwrap();
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    _logger.LogError(ex, "Unhandled exception in enable pins loop");
            }
        }

        public void Shutdown(McuShutdownMessage message)
        {
            if (_managerShutdownReason != null)
                return;
            if (message.Exception != null)
                message = new McuShutdownMessage
                {
                    Mcu = message.Mcu,
                    Reason = $"{message.Reason} (Exception: {message.Exception.Message})",
                    Exception = message.Exception
                };
            if (Interlocked.CompareExchange(ref _managerShutdownReason, message, null) != null)
                return;
            _logger.LogInformation($"Manager shutdown called: {message}. Stack trace: {new StackTrace(true)}");
            Task.Run(async () =>
            {
                try
                {
                    await ShutdownEvent.Invoke(message, _runningCancelSource.Token);
                }
                catch (Exception ex)
                {
                    if (!_runningCancelSource.IsCancellationRequested)
                        _logger.LogError(ex, $"Unhandled exception while invoking shutdown: {message}");
                }
            });
        }

        public ILogger<T> CreateLogger<T>()
            => _loggerFactory.CreateLogger<T>();

        public void RegisterSetup(IMcu? mcu, Func<CancellationToken, ValueTask> handler)
        {
            lock (_setupHandlers)
            {
                _setupHandlers.Add((mcu, handler));
            }
        }

        public void RegisterSetup(IMcu? mcu, Func<CancellationToken, Task> handler)
        {
            lock (_setupHandlers)
            {
                _setupHandlers.Add((mcu, handler));
            }
        }

        public IMcuClockSync GetClockSync(string alias)
            => _mcuItems[alias].Mcu.ClockSync;

        public (string Key, string Message)[] GetConnectionStatus()
        {
            List<(string Key, string Message)>? messages = null;
            foreach (var mcu in _mcuItems.Values)
            {
                if (mcu.Mcu.IsUpdatingFirmware)
                {
                    messages ??= new();
                    messages.Add(($"MCU {mcu.Mcu.Name}", "Updating firmware!"));
                }
                else
                {
                    var ex = mcu.Mcu.CurrentError;
                    if (ex != null)
                    {
                        messages ??= new();
                        messages.Add(($"MCU {mcu.Mcu.Name}", ex.Message));
                    }
                }
            }
            return messages?.Count > 0 ? messages.ToArray() : [];
        }

        public McuPinDescription ParsePin(string description, bool canInvert = false, bool canPullup = false)
        {
            if (string.IsNullOrEmpty(description))
                throw new ArgumentException("Pin description is empty or null", nameof(description));
            var res = McuPinDescription.Parse(description, canInvert, canPullup);
            var mcu = _mcuItems.GetValueOrDefault(res.McuAlias);
            if (mcu == null)
                throw new ArgumentException($"Pin was requested for unknown MCU '{res.McuAlias}'. Description: {description}");
            return new McuPinDescription(mcu.Mcu, res.PinName, res.Invert, res.Pullup);
        }

        public McuBusDescription ParseBus(string description)
        {
            (var mcuAlias, var busName) = McuBusDescription.Parse(description);
            var mcu = _mcuItems.GetValueOrDefault(mcuAlias);
            if (mcu == null)
                throw new ArgumentException($"Bus was requested for unknown MCU '{mcuAlias}'. Description: {description}");
            return new McuBusDescription(mcu.Mcu, busName);
        }

        public abstract McuPinDescription ClaimPin(McuPinType type, string description, bool canInvert = false, bool canPullup = false, string? shareType = null);

        public abstract McuBusDescription ClaimBus(string description, string? shareType = null);

        public LockMasterQueueDisposable LockMasterQueue()
        {
            Monitor.Enter(_queueMasterLock);
            return new LockMasterQueueDisposable(this);
        }

        public bool IsMasterQueueLocked()
            => Monitor.IsEntered(_queueMasterLock);

        private void QueueMasterEnd()
        {
            Monitor.Exit(_queueMasterLock);
        }

        public virtual bool TryCollectGarbageBlocking()
        {
            PrinterGC.CollectGarbageBlocking();
            return true;
        }
    }
}
