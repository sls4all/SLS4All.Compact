// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.McuClient.Pins.Tmc2208;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Security;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.McuClient.Sensors
{
    public class HysteresisHeaterOptions
    {
        public required string HeaterPin { get; set; }
        public required float MaxDelta { get; set; }
        public TimeSpan MinHeatPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxHeatTime { get; set; } = TimeSpan.FromSeconds(10);
        public double SetCycleFrequency { get; set; } = 2;
        public double PowerConsumption { get; set; } = 0;
        public int PowerManagerPriority { get; set; } = 0;
        public float OnFactor { get; set; } = 1.0f;
    }

    public sealed class HysteresisHeater : IMcuHeater
    {
        private readonly IOptions<HysteresisHeaterOptions> _options;
        private readonly Lock _locker = new();
        private readonly McuManager _manager;
        private readonly IMcuTemperatureSensor _sensor;
        private readonly string _name;
        private readonly McuPinDescription _heaterDesc;
        private readonly IMcuOutputPin _heater;
        private readonly ReferenceCounter _validationSupressor;
        private volatile StrongBox<float>? _target;
        private bool _targetReached;
        private bool _isReady;
        
        private bool _lastSetValue;
        private SystemTimestamp _lastSetTime;

        public McuTemperatureSensorData? CurrentValue => _sensor.CurrentValue;
        public AsyncEvent<McuTemperatureSensorData> ReadEvent => _sensor.ReadEvent;

        public float? Target
        {
            get => _target?.Value;
            set
            {
                if (value == Target)
                    return;
                lock (_locker)
                {
                    _target = value != null ? new StrongBox<float>(value.Value) : null;
                    _targetReached = false;
                    Update(null);
                }
            }
        }

        public (float? Target, bool Reached) TargetReached
        {
            get
            {
                lock (_locker)
                {
                    return (_target?.Value, _targetReached);
                }
            }
        }
        public bool IsValidationSupressed => _validationSupressor.IsIncremented;

        public HysteresisHeater(
            IOptions<HysteresisHeaterOptions> options,
            McuManager manager,
            IMcuTemperatureSensor sensor,
            string name)
        {
            _options = options;
            _manager = manager;
            _sensor = sensor;
            _name = name;

            var o = options.Value;
            _validationSupressor = new();
            _heaterDesc = manager.ClaimPin(McuPinType.Digital, o.HeaterPin, canInvert: true);
            _heater = _heaterDesc.SetupPin($"heater-{name}");
            _heater.SetupMaxDuration(o.MaxHeatTime);
            _manager.PowerManager.SetupPin(_heater, o.PowerConsumption, o.PowerManagerPriority, PowerPinType.NotSet);

            manager.RegisterSetup(null, OnSetup);
            _sensor.ReadEvent.AddHandler(OnRead);
        }

        private void Update(McuTemperatureSensorData? current)
        {
            lock (_locker)
            {
                if (!_isReady)
                    return;
                if (current == null)
                    current = _sensor.CurrentValue;
                var options = _options.Value;
                var target = Target;
                bool enable;
                if (current == null || target == null)
                    enable = false;
                else
                {
                    if (current.Temperature >= target.Value)
                        _targetReached = true;
                    enable =
                        ((_lastSetValue && current.Temperature < target.Value + options.MaxDelta) ||
                         (!_lastSetValue && current.Temperature < target.Value - options.MaxDelta));
                }
                var time = SystemTimestamp.Now;
                if (time - _lastSetTime < options.MinHeatPeriod && _lastSetValue == enable)
                    return; // No significant change in value - can suppress update
                if (time - _lastSetTime < TimeSpan.FromSeconds(1.0f / options.SetCycleFrequency))
                    return; // too fast
                _lastSetTime = time;
                _lastSetValue = enable;
                _manager.PowerManager.Set(_heater, enable ? options.OnFactor : 0);
            }
        }

        private ValueTask OnSetup(CancellationToken token)
        {
            lock (_locker)
            {
                _isReady = true;
                Update(null);
            }
            return ValueTask.CompletedTask;
        }

        private ValueTask OnRead(McuTemperatureSensorData data, CancellationToken cancel)
        {
            Update(data);
            return ValueTask.CompletedTask;

        }

        public IDisposable SupressValidation()
            => _validationSupressor.Increment();

        public override string ToString()
            => $"{_name} [HysteresisHeater]";
    }
}
