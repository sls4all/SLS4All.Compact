// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SLS4All.Compact.McuClient.Pins
{
    public sealed class McuDigitalPin : IMcuOutputPin
    {
        private readonly string _name;
        private readonly McuPinDescription _pin;
        private readonly bool _invert;
        private volatile float _currentValue;
        private int _oid;
        private bool _isStatic;
        private bool _startAlreadyInvertedValue;
        private bool _shutdownAlreadyInvertedValue;
        private TimeSpan _maxDuration;
        private (McuCommand cmd, int clockIndex, int valueIndex) _set = (McuCommand.PlaceholderCommand, 0, 0);
        private (McuCommand cmd, int valueIndex) _update = (McuCommand.PlaceholderCommand, 0);
        private McuSendResult? _updateResult;
        private long _lastClock;
        private volatile bool _hasBuiltConfig;

        public McuPinDescription Pin => _pin;
        public IMcu Mcu => _pin.Mcu;
        public McuPinValue CurrentValue => _currentValue;
        public string Name => _name;

        public McuDigitalPin(string name, McuPinDescription pin)
        {
            _name = name;
            _pin = pin;
            _invert = pin.Invert;
            _startAlreadyInvertedValue = _shutdownAlreadyInvertedValue = _invert;
            _maxDuration = pin.MaxDuration ?? IMcuOutputPin.DefaultMaxDuration;
            _isStatic = false;

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
            _startAlreadyInvertedValue = startValue.Get(_invert).IsFuzzyEnabled;
            _shutdownAlreadyInvertedValue = shutdownValue.Get(_invert).IsFuzzyEnabled;
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
            SetAtClock(value, priority, timestamp.Clock);
        }

        private void SetAtClock(McuPinValue value, int priority, long clock)
        {
            if (_isStatic)
                throw new InvalidOperationException($"Cannot change value of static pin {_pin}");
            if (!_hasBuiltConfig)
            {
                if (_currentValue == value.Single)
                    return;
                else
                    throw new InvalidOperationException($"Has not yet built config");
            }
            _currentValue = value.Single;
            if (clock == 0)
            {
                lock (_update.cmd)
                {
                    var occasion = new McuOccasion(clock, clock);
                    _lastClock = clock;
                    _update.cmd[_update.valueIndex] = value.Get(_invert).IsFuzzyEnabled ? 1 : 0;
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
                    _set.cmd[_set.valueIndex] = value.Get(_invert).IsFuzzyEnabled ? 1 : 0;
                    Mcu.Send(_set.cmd, priority, occasion);
                }
            }
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            _currentValue = _startAlreadyInvertedValue ? 1 : 0;
            _oid = commands.CreateOid();
            if (_isStatic)
            {
                commands.Add(Mcu.LookupCommand("set_digital_out pin=%u value=%c").Bind(
                    Mcu.Config.GetPin(_pin.Pin),
                    _startAlreadyInvertedValue ? 1 : 0));
            }
            else
            {
                commands.Add(Mcu.LookupCommand("config_digital_out oid=%c pin=%u value=%c default_value=%c max_duration=%u").Bind(
                    _oid,
                    Mcu.Config.GetPin(_pin.Pin),
                    _startAlreadyInvertedValue ? 1 : 0,
                    _shutdownAlreadyInvertedValue ? 1 : 0,
                    checked((int)Mcu.ClockSync.GetClockDuration(_maxDuration))));

                _update.cmd = Mcu.LookupCommand(_pin.AllowInShutdown ? "update_digital_out_in_shutdown oid=%c value=%c" : "update_digital_out oid=%c value=%c")
                    .Bind("oid", _oid)
                    .Bind("value", _startAlreadyInvertedValue ? 1 : 0);
                _update.valueIndex = _update.cmd.GetArgumentIndex("value");

                commands.Add(_update.cmd.Clone(), onRestart: true);

                _set.cmd = Mcu.LookupCommand("schedule_digital_out oid=%c clock=%u value=%c")
                    .Bind("oid", _oid);
                _set.clockIndex = _set.cmd.GetArgumentIndex("clock");
                _set.valueIndex = _set.cmd.GetArgumentIndex("value");
            }
            _hasBuiltConfig = true;
            return ValueTask.CompletedTask;
        }

        public override string ToString()
            => $"{_name} ({_pin.Key})";
    }
}
