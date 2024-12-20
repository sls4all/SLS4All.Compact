// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet.Messages.Connection;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Power;
using SLS4All.Compact.McuClient.Pins;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.IsolatedStorage;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public class McuPowerManagerOptions
    {
        public double BaseConsumption { get; set; }
        public double MaxConsumption { get; set; }
        public TimeSpan SwitchPeriod { get; set; } = TimeSpan.FromSeconds(1);
    }

    public class McuPowerManager : IMcuPowerManager
    {
        private sealed class PinInfo : IComparable<PinInfo>
        {
            public required IMcuOutputPin Pin { get; init; }
            public double MaxConsumptionWatts { get; set; }
            public float Value { get; set; }
            public SystemTimestamp OnTime { get; set; }
            public SystemTimestamp PowerTime { get; set; }
            public TimeSpan PowerDuration { get; set; }
            public bool HasProcessed { get; set; }
            public TimeSpan PowerDebt { get; set; }
            public int Priority { get; set; }
            public PowerPinType Type { get; set; }
            public long PowerConsumption { get; set; }

            public int CompareTo(PinInfo? other)
            {
                Debug.Assert(other != null);
                if (this.Priority > other.Priority)
                    return -1;
                if (this.Priority < other.Priority)
                    return 1;
                if (this.PowerDebt > other.PowerDebt)
                    return -1;
                if (this.PowerDebt < other.PowerDebt)
                    return 1;
                return 0;
            }

            public override string ToString()
                => $"{Pin}; Priority={Priority}; PowerDebt={PowerDebt}";
        }

        private readonly ILogger<McuPowerManager> _logger;
        private readonly IOptions<McuPowerManagerOptions> _options;
        private readonly IPrinterSettings _printerSettings;
        private readonly McuManager _manager;
        private readonly Dictionary<IMcuOutputPin, PinInfo> _infos;
        private readonly Lock _syncRoot = new();
        private readonly long _baseConsumption = 0;
        private readonly List<PinInfo> _switchList;
        private readonly List<PinInfo> _powerOffList;
        private volatile PrinterPowerSettingsSnapshot _powerSettings;
        private long _currentConsumption = 0;
        private long _maxConsumption = 0;

        public McuPowerManager(
            IOptions<McuPowerManagerOptions> options,
            McuManager manager,
            IPrinterSettings printerSettings)
        {
            _logger = manager.CreateLogger<McuPowerManager>();
            _options = options;
            _manager = manager;
            _printerSettings = printerSettings;

            var o = options.Value;
            _switchList = new();
            _powerOffList = new();
            _infos = new();
            _powerSettings = _printerSettings.Power;
            _baseConsumption = GetConsumption(o.BaseConsumption, PowerPinType.NotSet, _powerSettings);
            _currentConsumption = 0;
            SetTotalMaxConsumption(o.MaxConsumption);
            Task.Run(SwitchThread);
        }

        public PowermanState GetState()
        {
            lock (_syncRoot)
            {
                var maxPower = GetWatts(_maxConsumption + _baseConsumption);
                var currentPower = GetWatts(_currentConsumption + _baseConsumption);
                var poweredOnDesc = new StringBuilder();
                var required = 0L;
                var powerSettings = _powerSettings;
                foreach (var info in _infos.Values)
                {
                    if (!info.OnTime.IsEmpty)
                        required += info.PowerConsumption;
                    if (!info.PowerTime.IsEmpty)
                    {
                        if (poweredOnDesc.Length != 0)
                            poweredOnDesc.Append("; ");
                        poweredOnDesc.Append(info.Pin.Name);
                        poweredOnDesc.Append('[');
                        poweredOnDesc.Append(info.Priority);
                        poweredOnDesc.Append(']');
                        poweredOnDesc.Append('=');
                        poweredOnDesc.Append(GetWatts(info.PowerConsumption));
                        poweredOnDesc.Append('W');
                    }
                }
                var requiredPower = GetWatts(required + _baseConsumption);
                return new PowermanState(
                    maxPower,
                    currentPower,
                    requiredPower,
                    poweredOnDesc.ToString());
            }
        }

        public void SetTotalMaxConsumption(double watts)
        {
            var powerSettings = _powerSettings;
            var maxConsumption = GetConsumption(watts, PowerPinType.NotSet, powerSettings) - _baseConsumption;
            if (maxConsumption < 0)
                maxConsumption = 0;
            if (_maxConsumption == maxConsumption)
                return;
            lock (_syncRoot)
            {
                var now = SystemTimestamp.Now;
                foreach (var info in _infos.Values)
                    TryPowerOffInner(info, now, powerSettings);
                _maxConsumption = maxConsumption;
                SwitchPinsInner(powerSettings);
            }
        }

        public void SetupPin(IMcuOutputPin pin, double watts, int priority, PowerPinType type)
        {
            lock (_syncRoot)
            {
                var powerSettings = _powerSettings;
                var now = SystemTimestamp.Now;
                ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(_infos, pin, out var exists);
                if (info == null)
                    info = new PinInfo() { Pin = pin };
                var wasPoweredOff = TryPowerOffInner(info, now, powerSettings);
                info.MaxConsumptionWatts = watts;
                info.Priority = priority;
                info.Type = type;
                if (wasPoweredOff)
                    TryPowerOnInner(info, now, false, powerSettings);
            }
        }

        private int? GetMinPoweredPriorityInner()
        {
            int? min = null;
            foreach (var info in _infos.Values)
            {
                if (!info.PowerTime.IsEmpty)
                {
                    if (min == null || min.Value > info.Priority)
                        min = info.Priority;
                }
            }
            return min;
        }

        private int? GetMaxUnpoweredPriorityInner()
        {
            int? max = null;
            foreach (var info in _infos.Values)
            {
                if (info.PowerTime.IsEmpty)
                {
                    if (max == null || max.Value < info.Priority)
                        max = info.Priority;
                }
            }
            return max;
        }

        public void Set(IMcuOutputPin pin, McuPinValue value)
        {
            lock (_syncRoot)
            {
                var now = SystemTimestamp.Now;
                var powerSettings = _powerSettings;
                ref var info = ref CollectionsMarshal.GetValueRefOrAddDefault(_infos, pin, out _);
                if (info == null)
                    info = new PinInfo() { Pin = pin };
                if (value.IsNonZero) // should be on
                {
                    if (info.OnTime.IsEmpty)
                    {
                        info.OnTime = now;
                        info.PowerDuration = TimeSpan.Zero;
                    }
                    info.Value = value.Single;
                    var poweredOn = TryPowerOnInner(info, now, force: true, powerSettings); // NOTE: force, so we update MCU next on period (max duration)
                    if (!poweredOn && GetMinPoweredPriorityInner() < info.Priority) // if there is anything with lower priority to sacrifice, switch now
                        SwitchPinsInner(powerSettings, explicitPin: info);
                }
                else
                {
                    info.Value = 0;
                    info.OnTime = default;
                    TryPowerOffInner(info, now, powerSettings);
                    info.PowerDuration = TimeSpan.Zero;
                }
            }
        }

        private bool TryPowerOnInner(PinInfo info, SystemTimestamp now, bool force, PrinterPowerSettingsSnapshot powerSettings)
        {
            if (info.PowerTime.IsEmpty) // not powered on
            {
                var infoMaxConsumption = GetConsumption(info.MaxConsumptionWatts, info.Type, powerSettings);
                if (_currentConsumption + infoMaxConsumption <= _maxConsumption)
                {
                    // we have have enough power to spare, switch on immediately
                    info.PowerTime = now;
                    info.PowerConsumption = infoMaxConsumption;
                    _currentConsumption += infoMaxConsumption;
                    info.Pin.Set(info.Value, McuCommandPriority.Default, McuTimestamp.Immediate(info.Pin.Mcu));
                    return true;
                }
                else
                    return false;
            }
            else
            {
                if (force)
                    info.Pin.Set(info.Value, McuCommandPriority.Default, McuTimestamp.Immediate(info.Pin.Mcu));
                return true;
            }
        }

        private bool TryPowerOffInner(PinInfo info, SystemTimestamp now, PrinterPowerSettingsSnapshot powerSettings)
        {
            if (!info.PowerTime.IsEmpty)
            {
                // was enabled, disable
                info.PowerDuration += now - info.PowerTime;
                info.PowerTime = default;
                Debug.Assert(_currentConsumption >= info.PowerConsumption);
                _currentConsumption -= info.PowerConsumption;
                info.Pin.Set(0, McuCommandPriority.Default, McuTimestamp.Immediate(info.Pin.Mcu));
                return true;
            }
            else
                return false;
        }

        private async void SwitchThread()
        {
            var cancel = _manager.RunningCancel;
            var options = _options.Value;
            try
            {
                var timer = new PeriodicTimer(options.SwitchPeriod);
                while (await timer.WaitForNextTickAsync(cancel))
                {
                    lock (_syncRoot)
                    {
                        var powerSettings = _printerSettings.Power;
                        _powerSettings = powerSettings;
                        SwitchPinsInner(powerSettings);
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                {
                    var msg = $"Unhandled exception in McuPowerManager";
                    _logger.LogCritical(ex, msg);
                    _manager.Shutdown(new Messages.McuShutdownMessage
                    {
                        Mcu = null,
                        Exception = ex,
                        Reason = msg,
                    });
                }
            }
        }

        private void SwitchPinsInner(PrinterPowerSettingsSnapshot powerSettings, PinInfo? explicitPin = null, bool powerOnOnly = false)
        {
            var now = SystemTimestamp.Now;
            var somethingPoweredOffButShouldBeOn = false;
            _switchList.Clear();
            foreach (var info in _infos.Values)
            {
                if (info.OnTime.IsEmpty)
                {
                    Debug.Assert(info.PowerTime.IsEmpty);
                    continue;
                }
                var onDuration = now - info.OnTime;
                var powerDuration = info.PowerDuration;
                if (!info.PowerTime.IsEmpty)
                    powerDuration += now - info.PowerTime;
                else
                    somethingPoweredOffButShouldBeOn = true;
                Debug.Assert(powerDuration <= onDuration);
                info.PowerDebt = onDuration - powerDuration;
                info.HasProcessed = false;
                _switchList.Add(info);
            }

            if (somethingPoweredOffButShouldBeOn)
            {
                _switchList.Sort();
                for (int i = 0; i < _switchList.Count; i++)
                {
                    var info = _switchList[i];
                    Debug.Assert(!info.OnTime.IsEmpty); // should be on if it ended here

                    if (info.HasProcessed) // already touched in some of the previous loops
                        continue;

                    if (!info.PowerTime.IsEmpty) // powered on
                    {
                        // since we are going from items with most debt (and most prioroty) to least
                        // remove this item from processing an keep as it iss
                        info.HasProcessed = true;
                        continue;
                    }

                    // if explicit pin we want to switch is given, skip any other
                    if (explicitPin != null && explicitPin != info)
                    {
                        info.HasProcessed = true;
                        continue;
                    }

                    // powered off, has a lot of debt, try to power off something with less debt
                    var available = _maxConsumption - _currentConsumption;
                    Debug.Assert(available >= 0);
                    var remaining = info.PowerConsumption - available;
                    if (remaining > 0)
                    {
                        _powerOffList.Clear();
                        for (int q = _switchList.Count - 1; q >= 0 && remaining > 0; q--)
                        {
                            var candidate = _switchList[q];
                            var candidateMaxConsumption = GetConsumption(candidate.MaxConsumptionWatts, candidate.Type, powerSettings);
                            if (candidate.PowerTime.IsEmpty || candidate.HasProcessed) // not powered or already processed
                                continue;
                            Debug.Assert(candidate != info);
                            if (candidateMaxConsumption > 0)
                            {
                                remaining -= candidateMaxConsumption;
                                _powerOffList.Add(candidate);
                            }
                        }
                        if (remaining > 0) // nothing can be switched off to power this one on
                            continue;
                    }

                    // switch power
                    if (powerOnOnly && _powerOffList.Count > 0)
                    {
                        // if we are in mode for powering on only, just mark the pin as processed, do not actually disable anything if needed
                        info.HasProcessed = true;
                        continue;
                    }

                    foreach (var other in _powerOffList)
                    {
                        other.HasProcessed = true;
                        TryPowerOffInner(other, now, powerSettings);
                    }
                    info.HasProcessed = true;
                    TryPowerOnInner(info, now, false, powerSettings);
                }
            }
        }

        private static long GetConsumption(double watts, PowerPinType type, PrinterPowerSettingsSnapshot powerSettings)
        {
            if (watts < 0)
                throw new ArgumentOutOfRangeException(nameof(watts));
            if (type == PowerPinType.Halogen)
                watts = watts * (double)powerSettings.HalogenMaxPercent / 100;
            return (long)Math.Ceiling(watts * 1000);
        }

        private static double GetWatts(long consumption)
            => consumption * 0.001;
    }
}
