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
        private (McuCommand Cmd, McuCommandArgument Clock, McuCommandArgument OnTicks, McuCommandArgument OffTicks) _set = (McuCommand.PlaceholderCommand, default, default, default);
        private (McuCommand Cmd, McuCommandArgument OnTicks, McuCommandArgument OffTicks) _update = (McuCommand.PlaceholderCommand, default, default);
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
                lock (_update.Cmd)
                {
                    var occasion = new McuOccasion(clock, clock);
                    _lastClock = clock;
                    _update.OnTicks.Value = intValue;
                    _update.OffTicks.Value = _cycleTicks - intValue;
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
                    _set.OnTicks.Value = intValue;
                    _set.OffTicks.Value = _cycleTicks - intValue;
                    Mcu.Send(_set.Cmd, priority, occasion);
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

                _update = Mcu.LookupCommand("update_soft_pwm_out oid=%c on_ticks=%u off_ticks=%u", "on_ticks", "off_ticks")
                    .Bind("oid", _oid)
                    .Bind("on_ticks", svalue)
                    .Bind("off_ticks", _cycleTicks - svalue);

                commands.Add(_update.Cmd.Clone(), onInit: true);

                _set = Mcu.LookupCommand("schedule_soft_pwm_out oid=%c clock=%u on_ticks=%u off_ticks=%u", "clock", "on_ticks", "off_ticks")
                    .Bind("oid", _oid);
            }
            return ValueTask.CompletedTask;
        }

        public override string ToString()
            => $"{_name} ({_pin.Key})";
    }
}
