// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SLS4All.Compact.McuClient.Pins
{
    public sealed class McuDimmerPin : IMcuOutputPin
    {
        private readonly string _name;
        private readonly McuPinDescription _pin;
        private readonly McuPinDescription _sensor;
        private volatile float _currentValue;
        private int _oid;
        private float _startValue;
        private float _shutdownValue;
        private TimeSpan _maxDuration;
        private (McuCommand Cmd, McuCommandArgument Clock, McuCommandArgument Value) _set = (McuCommand.PlaceholderCommand, default, default);
        private (McuCommand Cmd, McuCommandArgument Value) _update = (McuCommand.PlaceholderCommand, default);
        private long _lastClock;
        private int _dimmerMax;
        private McuSendResult? _updateResult;

        public McuPinDescription Pin => _pin;
        public IMcu Mcu => _pin.Mcu;
        public McuPinValue CurrentValue => _currentValue;
        public string Name => _name;

        public McuDimmerPin(string name, McuPinDescription pin)
        {
            var sensor = pin.SensorPin ?? throw new ArgumentException($"{nameof(pin.SensorPin)} must be set for {pin}");
            if (pin.Mcu != sensor.Mcu)
                throw new ArgumentException($"Pin ({pin}) and sensor ({sensor}) pins must be on the same MCU");
            _name = name;
            _pin = pin;
            _sensor = sensor;
            _startValue = _shutdownValue = 0;
            _maxDuration = pin.MaxDuration ?? IMcuOutputPin.DefaultMaxDuration;

            Mcu.RegisterConfigCommand(BuildConfig);
        }

        public void SetupMaxDuration(TimeSpan maxDuration)
        {
            _maxDuration = maxDuration;
        }

        public void SetupStartValue(McuPinValue startValue, McuPinValue shutdownValue, bool isStatic)
        {
            if (isStatic)
                throw new NotSupportedException($"Static dimmer pins are not supported. Pin: {_pin}");
            _startValue = startValue.Single;
            _shutdownValue = shutdownValue.Single;
        }

        public void Set(McuPinValue value, int priority, SystemTimestamp timestamp)
        {
            var clock = Mcu.ClockSync.GetClock(timestamp);
            SetAtClock(value, priority, clock);
        }

        public void Set(McuPinValue value, int priority, McuTimestamp timestamp)
        {
            if (timestamp.Mcu != Mcu)
                throw new ArgumentException($"Timestamp {timestamp} is for different MCU than {Mcu}");
            SetAtClock(value, McuCommandPriority.Default, timestamp.Clock);
        }

        private void SetAtClock(McuPinValue value, int priority, long clock)
        {
            _currentValue = value.Single;
            var intValue = (int)MathF.Round(value.Single * _dimmerMax);
            if (clock == 0)
            {
                lock (_update.Cmd)
                {
                    var occasion = new McuOccasion(clock, clock);
                    _lastClock = clock;
                    _update.Value.Value = intValue;
                    _updateResult = Mcu.Send(_update.Cmd, priority, occasion, cancelFirst: _updateResult);
                }
            }
            else 
            {
                lock (_set.Cmd)
                {
                    var occasion = new McuOccasion(_lastClock, clock);
                    _lastClock = clock;
                    _set.Clock.Value = clock;
                    _set.Value.Value = intValue;
                    Mcu.Send(_set.Cmd, priority, occasion);
                }
            }
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            // TODO: add command for restart, shutdown, and maxDuration

            _currentValue = _startValue;
            _oid = commands.CreateOid();
            _dimmerMax = Mcu.Config.GetConstInt32("DIMMER_MAX");
            var value = (int)(Math.Clamp(_startValue, 0, 1) * _dimmerMax);
            commands.Add(Mcu.LookupCommand("config_dimmer_out oid=%c sensor_pin=%u pin=%u value=%hu invert=%c max_duration=%u").Bind(
                _oid,
                Mcu.Config.GetPin(_sensor.Pin),
                Mcu.Config.GetPin(_pin.Pin),
                value,
                _pin.Invert ? 1 : 0,
                checked((int)Mcu.ClockSync.GetClockDuration(_maxDuration))));

            _update = Mcu.LookupCommand("update_dimmer_out oid=%c value=%hu", "value")
                .Bind("oid", _oid)
                .Bind("value", value);

            commands.Add(_update.Cmd.Clone(), onRestart: true);

            _set = Mcu.LookupCommand("schedule_dimmer_out oid=%c clock=%u value=%hu", "clock", "value")
                .Bind("oid", _oid);
            return ValueTask.CompletedTask;
        }

        public override string ToString()
            => $"{_name} ({_pin.Key})";
    }
}
