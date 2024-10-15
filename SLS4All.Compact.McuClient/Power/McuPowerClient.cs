// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Data;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Linq;
using System.Net.NetworkInformation;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Serialization;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using SLS4All.Compact.Storage.PrinterSettings;

namespace SLS4All.Compact.Power
{
    public class McuPowerClientOptions
    {
        public TimeSpan LowFrequencyPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public string LaserId { get; set; } = "laser";
        public TimeSpan SetPinTime { get; set; } = TimeSpan.FromSeconds(0.1);
        public TimeSpan LaserChangedNotifyPeriod { get; set; } = TimeSpan.FromSeconds(1);
    }

    public sealed class McuPowerClient : BackgroundThreadService, IPowerClient
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<McuPowerClientOptions> _options;
        private readonly IMediator _mediator;
        private readonly McuPrinterClient _printerClient;
        private readonly PeriodicTimer _lowFrequencyTimer;
        private readonly string _laserId;
        private readonly TimeSpan _laserChangedNotifyPeriod;
        private SystemTimestamp _laserNotifiedTimestamp;

        private readonly object _stateLock = new object();
        private readonly PowermanState _fallbackPowermanState;
        private volatile PowerState _lowFrequencyState;
        private readonly Dictionary<string, (double value, SystemTimestamp timestamp, double prevNonZeroValue, SystemTimestamp prevZeroingTimestamp)> _stateDict;

        private readonly object _setPowerFormattersLock = new();
        private readonly static object _setPowerLaserFormatterTag = new();
        private readonly DelegatedCodeFormatter _setPowerLaserFormatter;
        private volatile FrozenDictionary<string, DelegatedCodeFormatter> _setPowerFormatters;

        public PowerState CurrentState => _lowFrequencyState;
        public AsyncEvent<PowerState> StateChangedLowFrequency { get; } = new();
        public AsyncEvent<PowerState> StateChangedHighFrequency { get; } = new();
        public string LaserId => _laserId;

        public McuPowerClient(
            ILogger<McuPowerClient> logger, 
            IOptionsMonitor<McuPowerClientOptions> options,
            IMediator mediator, 
            McuPrinterClient printerClient)
            : base(logger)
        {
            _logger = logger;
            _options = options;
            _mediator = mediator;
            _printerClient = printerClient;
            
            var o = options.CurrentValue;
            _laserId = o.LaserId;
            _laserChangedNotifyPeriod = o.LaserChangedNotifyPeriod;
            _fallbackPowermanState = new PowermanState(0, 0, 0, "");
            _lowFrequencyState = new(Array.Empty<PowerEntry>(), _fallbackPowermanState);
            _lowFrequencyTimer = new PeriodicTimer(o.LowFrequencyPeriod);
            _stateDict = new();

            _setPowerFormatters = FrozenDictionary<string, DelegatedCodeFormatter>.Empty;
            _setPowerLaserFormatter = GetSetPowerFormatter(_laserId);

            printerClient.ManagerSetEvent.AddHandler(OnManagerSet);
        }

        private ValueTask OnManagerSet(McuManager manager, CancellationToken token)
        {
            lock (_stateLock)
            {
                var now = SystemTimestamp.Now;
                foreach (var pair in manager.OutputPins)
                {
                    UpdatePowerDictInner(pair.Key, pair.Value.CurrentValue.Single, now);
                }
            }
            return ValueTask.CompletedTask;
        }

        private DelegatedCodeFormatter GetSetPowerFormatter(string id)
        {
            if (!_setPowerFormatters.TryGetValue(id, out var res))
            {
                lock (_setPowerFormattersLock)
                {
                    if (!_setPowerFormatters.TryGetValue(id, out res))
                    {
                        var clone = _setPowerFormatters.ToDictionary();
                        res = new DelegatedCodeFormatter((cmd, hidden, context, cancel) =>
                            SetPower(id, cmd.Arg1, cmd.Arg2 != 0, hidden: hidden, context: context, cancel: cancel),
                            cmd => string.Create(CultureInfo.InvariantCulture, $"SETPOWER ID={id} VALUE={cmd.Arg1} PRECISE={cmd.Arg2}"),
                            id == _laserId ? _setPowerLaserFormatterTag : null);
                        clone.Add(id, res);
                        _setPowerFormatters = clone.ToFrozenDictionary();
                    }
                }
            }
            return res;
        }

        public ValueTask SetPower(string id, double value, bool setImmediate, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            var options = _options.CurrentValue;
            var pin = manager.OutputPins[id];
            var now = SystemTimestamp.Now;
            if (setImmediate)
            {
                pin.SetImmediate(
                    (float)value,
                    McuCommandPriority.Default);
            }
            else
            {
                using (var master = manager.LockMasterQueue())
                {
                    var timestamp = master[pin];
                    if (timestamp.IsEmpty || timestamp.ToSystem() < now + options.SetPinTime)
                        timestamp = McuTimestamp.FromSystem(pin.Mcu, now + options.SetPinTime);
                    else
                        timestamp += options.SetPinTime;
                    pin.Set(
                        (float)value,
                        McuCommandPriority.Default,
                        timestamp);
                    master[pin] = timestamp;
                }
            }

            return UpdatePowerDictWithNotify(id, value, now, cancel);
        }

        public ValueTask SetPowerCode(ChannelWriter<CodeCommand> channel, string id, double value, bool setImmediate, CancellationToken cancel = default)
        {
            var formatter = id == _laserId ? _setPowerLaserFormatter : GetSetPowerFormatter(id);
            return channel.WriteAsync(formatter.Create((float)value, setImmediate ? 1 : 0), cancel);
        }

        public bool IsSetLaserPowerCode(CodeCommand cmd, out double value)
        {
            if (DelegatedCodeFormatter.IsWithTag(cmd, _setPowerLaserFormatterTag))
            {
                value = cmd.Arg1;
                return true;
            }
            else
            {
                value = 0;
                return false;
            }
        }

        internal SystemTimestamp GetLastTimestamp(string id)
        {
            lock (_stateLock)
            {
                ref var value = ref CollectionsMarshal.GetValueRefOrNullRef(_stateDict, id);
                if (Unsafe.IsNullRef(ref value))
                    return default;
                else
                    return value.timestamp;
            }
        }

        private bool UpdatePowerDictInner(string id, double power, SystemTimestamp timestamp)
        {
            ref var value = ref CollectionsMarshal.GetValueRefOrAddDefault(_stateDict, id, out var exists);
            var powersChanged = value.value != power;
            if (power != 0)
                value.prevNonZeroValue = power;
            else if (value.value != 0)
                value.prevZeroingTimestamp = timestamp;
            value.value = power;
            value.timestamp = timestamp;
            return powersChanged;
        }

        public ValueTask UpdatePowerDictWithNotify(string id, double value, SystemTimestamp timestamp, CancellationToken cancel)
        {
            PowerState state;
            lock (_stateLock)
            {
                var powersChanged = UpdatePowerDictInner(id, value, timestamp);
                if (powersChanged) 
                {
                    if (id == _laserId) // laser changes are too frequent, filter some
                    {
                        if (!_laserNotifiedTimestamp.IsEmpty && timestamp - _laserNotifiedTimestamp <= _laserChangedNotifyPeriod)
                            return ValueTask.CompletedTask;
                        _laserNotifiedTimestamp = timestamp;
                    }
                    state = GetState();
                }
                else
                    return ValueTask.CompletedTask;
            }
            return StateChangedHighFrequency.Invoke(state, cancel);
        }

        private PowerEntry[] ReadEntriesNeedsLock()
        {
            var entries = new PowerEntry[_stateDict.Count];
            var i = 0;
            foreach ((var key, var value) in _stateDict)
                entries[i++] = new PowerEntry(value.timestamp, key, value.value);
            return entries;
        }

        private PowermanState? ReadPowermanStateNeedsLock()
        {
            var manager = _printerClient.ManagerIfReady;
            return manager?.PowerManager.GetState();
        }

        private PowerState GetState()
        {
            lock (_stateLock)
            {
                var entries = ReadEntriesNeedsLock();
                var powermanState = ReadPowermanStateNeedsLock();
                Array.Sort(entries, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id));
                return new PowerState(entries, powermanState ?? _fallbackPowermanState);
            }
        }
        
        public Task SetPowermanMax(double value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var manager = McuInitializeCommandContext.GetManager(_printerClient, context);
            manager.PowerManager.SetTotalMaxConsumption(value);
            return Task.CompletedTask;
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            while (true)
            {
                try
                {
                    PowerState state;
                    lock (_stateLock)
                    {
                        var now = SystemTimestamp.Now;
                        state = GetState();
                        _lowFrequencyState = state;
                        _laserNotifiedTimestamp = now;
                    }
                    await StateChangedHighFrequency.Invoke(state, cancel); // for laser
                    await StateChangedLowFrequency.Invoke(state, cancel);
                    await _mediator.Publish(state, cancel);
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to get/process low frequency data");
                }
                try
                {
                    await _lowFrequencyTimer.WaitForNextTickAsync(cancel);
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to wait for next period");
                }
            }
        }

        public bool TryGetRecentPower(string id, out bool isCurrent, out double power, SystemTimestamp now, TimeSpan? duration)
        {
            (double value, SystemTimestamp timestamp, double prevNonZeroValue, SystemTimestamp prevZeroingTimestamp) entry;
            lock (_stateLock)
            {
                if (!_stateDict.TryGetValue(id, out entry))
                {
                    power = 0;
                    isCurrent = false;
                    return false;
                }
            }
            if (now.IsEmpty)
                now = SystemTimestamp.Now;
            var threshold = now - (duration ?? TimeSpan.FromSeconds(2));
            if (entry.value != 0)
            {
                isCurrent = true;
                power = entry.value;
            }
            else if (!entry.prevZeroingTimestamp.IsEmpty && entry.prevZeroingTimestamp > threshold)
            {
                isCurrent = false;
                power = entry.prevNonZeroValue;
            }
            else
            {
                isCurrent = true;
                power = 0;
            }
            return true;
        }

        public bool HasRecentPower(string id, SystemTimestamp now = default, TimeSpan? duration = null)
            => TryGetRecentPower(id, out _, out var power, now, duration) && power != 0;

        public override void Dispose()
        {
            _printerClient.ManagerSetEvent.RemoveHandler(OnManagerSet);
            base.Dispose();
        }
    }
}
