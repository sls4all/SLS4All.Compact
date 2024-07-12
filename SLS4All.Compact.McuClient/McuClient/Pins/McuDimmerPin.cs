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
        private (McuCommand cmd, int clockIndex, int valueIndex) _set = (McuCommand.PlaceholderCommand, 0, 0);
        private (McuCommand cmd, int valueIndex) _update = (McuCommand.PlaceholderCommand, 0);
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
                lock (_update.cmd)
                {
                    var occasion = new McuOccasion(clock, clock);
                    _lastClock = clock;
                    _update.cmd[_update.valueIndex] = intValue;
                    _updateResult = Mcu.Send(_update.cmd, priority, occasion, cancelFirst: _updateResult);
                }
            }
            else 
            {
                lock (_set.cmd)
                {
                    var occasion = new McuOccasion(_lastClock, clock);
                    _lastClock = clock;
                    _set.cmd[_set.clockIndex] = clock;
                    _set.cmd[_set.valueIndex] = intValue;
                    Mcu.Send(_set.cmd, priority, occasion);
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

            _update.cmd = Mcu.LookupCommand("update_dimmer_out oid=%c value=%hu")
                .Bind("oid", _oid)
                .Bind("value", value);
            _update.valueIndex = _update.cmd.GetArgumentIndex("value");

            commands.Add(_update.cmd.Clone(), onRestart: true);

            _set.cmd = Mcu.LookupCommand("schedule_dimmer_out oid=%c clock=%u value=%hu")
                .Bind("oid", _oid);
            _set.clockIndex = _set.cmd.GetArgumentIndex("clock");
            _set.valueIndex = _set.cmd.GetArgumentIndex("value");
            return ValueTask.CompletedTask;
        }

        public override string ToString()
            => $"{_name} ({_pin.Key})";
    }
}
