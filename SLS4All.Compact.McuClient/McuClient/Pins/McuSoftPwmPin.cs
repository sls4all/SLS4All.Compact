// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SLS4All.Compact.McuClient.Pins
{
    public sealed class McuSoftPwmPin : IMcuOutputPin
    {
        private readonly string _name;
        private readonly McuPinDescription _pin;
        private readonly double _cycleTime;
        private volatile float _currentValue;
        private int _cycleTicks;
        private int _pwmMax;
        private bool _isStatic;
        private int _oid;
        private float _startValue;
        private float _shutdownValue;
        private TimeSpan _maxDuration;
        private (McuCommand cmd, int clockIndex, int onTicksIndex, int offTicksIndex) _set = (McuCommand.PlaceholderCommand, 0, 0, 0);
        private (McuCommand cmd, int onTicksIndex, int offTicksIndex) _update = (McuCommand.PlaceholderCommand, 0, 0);
        private long _lastClock;
        private McuSendResult? _updateResult;

        public McuPinDescription Pin => _pin;
        public IMcu Mcu => _pin.Mcu;
        public McuPinValue CurrentValue => _currentValue;
        public string Name => _name;

        public McuSoftPwmPin(string name, McuPinDescription pin)
        {
            _name = name;
            _pin = pin;
            _cycleTime = pin.CycleTime ?? throw new ArgumentException($"{nameof(pin.CycleTime)} must be set for {pin}");
            _startValue = _shutdownValue = pin.Invert ? 1 : 0;
            _maxDuration = pin.MaxDuration ?? IMcuOutputPin.DefaultMaxDuration;

            Mcu.RegisterConfigCommand(BuildConfig);
        }

        public void SetupMaxDuration(TimeSpan maxDuration)
        {
            _maxDuration = maxDuration;
        }

        public void SetupStartValue(McuPinValue startValue, McuPinValue shutdownValue, bool isStatic)
        {
            if (_isStatic && shutdownValue != startValue)
                throw new ArgumentException($"Static pins cannot have shutdown value. Pin: {_pin}");
            if (startValue != 0 && shutdownValue != 1)
                throw new InvalidOperationException($"Start/Shutdown value must be 0.0 or 1.0 on soft pwm for {_pin}");
            _startValue = startValue.Get(_pin.Invert).Single;
            _shutdownValue = shutdownValue.Get(_pin.Invert).Single;
            _isStatic = isStatic;
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
            if (_isStatic)
                throw new InvalidOperationException($"Cannot change value of static pin {_pin}");
            var intValue = (int)MathF.Round(value.Get(_pin.Invert).Single * _pwmMax);
            _currentValue = value.Single;
            if (clock == 0)
            {
                lock (_update.cmd)
                {
                    var occasion = new McuOccasion(clock, clock);
                    _lastClock = clock;
                    _update.cmd[_update.onTicksIndex] = intValue;
                    _update.cmd[_update.offTicksIndex] = _cycleTicks - intValue;
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
                    _set.cmd[_set.onTicksIndex] = intValue;
                    _set.cmd[_set.offTicksIndex] = _cycleTicks - intValue;
                    Mcu.Send(_set.cmd, priority, occasion);
                }
            }
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            _cycleTicks = checked((int)Mcu.ClockSync.GetClockDuration(_cycleTime));
            _currentValue = _startValue;
            _pwmMax = _cycleTicks;
            if (_isStatic)
            {
                commands.Add(Mcu.LookupCommand("set_digital_out pin=%u value=%c").Bind(
                    Mcu.Config.GetPin(_pin.Pin),
                    _startValue >= 0.5f ? 1 : 0));
            }
            else
            {
                _oid = commands.CreateOid();
                commands.Add(Mcu.LookupCommand("config_soft_pwm_out oid=%c pin=%u value=%c default_value=%c max_duration=%u").Bind(
                    _oid,
                    Mcu.Config.GetPin(_pin.Pin),
                    _startValue >= 0.5f ? 1 : 0,
                    _shutdownValue >= 0.5f ? 1 : 0,
                    checked((int)Mcu.ClockSync.GetClockDuration(_maxDuration))));
                var svalue = (int)MathF.Round(_startValue * _pwmMax);

                _update.cmd = Mcu.LookupCommand("update_soft_pwm_out oid=%c on_ticks=%u off_ticks=%u")
                    .Bind("oid", _oid)
                    .Bind("on_ticks", svalue)
                    .Bind("off_ticks", _cycleTicks - svalue);
                _update.onTicksIndex = _update.cmd.GetArgumentIndex("on_ticks");
                _update.offTicksIndex = _update.cmd.GetArgumentIndex("off_ticks");

                commands.Add(_update.cmd.Clone(), onInit: true);

                _set.cmd = Mcu.LookupCommand("schedule_soft_pwm_out oid=%c clock=%u on_ticks=%u off_ticks=%u")
                    .Bind("oid", _oid);
                _set.clockIndex = _set.cmd.GetArgumentIndex("clock");
                _set.onTicksIndex = _set.cmd.GetArgumentIndex("on_ticks");
                _set.offTicksIndex = _set.cmd.GetArgumentIndex("off_ticks");
            }
            return ValueTask.CompletedTask;
        }

        public override string ToString()
            => $"{_name} ({_pin.Key})";
    }
}
