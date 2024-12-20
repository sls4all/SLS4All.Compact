// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.VisualBasic;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Pins
{
    public sealed class McuButton : IMcuButton
    {
        private readonly string _name;
        private readonly McuPinDescription[] _pins;
        private readonly IMcu _mcu;
        private readonly int _invert;
        private readonly TaskQueue _buttonEventQueue;
        private readonly int _mask;
        private int _oid;
        private int _lastButton;
        private int _ackCount;
        private (McuCommand Cmd, McuCommandArgument Count) _ackCmd = (McuCommand.PlaceholderCommand, default);
        private McuSendResult? _ackResult;
        private static readonly TimeSpan _queryTime = TimeSpan.FromSeconds(0.002);
        private const int _retransmitCount = 50;

        public string Name => _name;
        public IMcu Mcu => _mcu;
        public AsyncEvent<int> ButtonEvent { get; } = new();

        public McuButton(string name, McuPinDescription[] pins)
        {
            if (pins.Length == 0)
                throw new ArgumentException($"Pins must be non-empty for button {name}");

            _name = name;
            _pins = (McuPinDescription[])pins.Clone();
            _buttonEventQueue = new();

            _mcu = null!;
            _invert = 0;
            _mask = 0;
            for (int i = 0; i < pins.Length; i++)
            {
                McuPinDescription? pin = pins[i];
                if (_mcu == null)
                    _mcu = pin.Mcu;
                else if (_mcu != pin.Mcu)
                    throw new ArgumentException($"Pins must be all on the same MCU for button {name}");
                if (pin.Invert)
                    _invert |= 1 << i;
                _mask = (_mask << 1) | 1;
            }

            Mcu.RegisterConfigCommand(BuildConfig);
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            _oid = commands.CreateOid();
            commands.Add(Mcu.LookupCommand("config_buttons oid=%c button_count=%c").Bind(
                _oid,
                _pins.Length));
            for (int i = 0; i < _pins.Length; i++)
            {
                var pin = _pins[i];
                commands.Add(Mcu.LookupCommand("buttons_add oid=%c pos=%c pin=%u pull_up=%c").Bind(
                    _oid,
                    i,
                    Mcu.Config.GetPin(pin.Pin),
                    pin.Pullup),
                    onInit: true);
            }
            _ackCmd = Mcu.LookupCommand("buttons_ack oid=%c count=%c", "count")
                .Bind("oid", _oid);

            commands.Add(Mcu.LookupCommand("buttons_query oid=%c clock=%u rest_ticks=%u retransmit_count=%c invert=%c").Bind(
                _oid,
                McuHelpers.GetQuerySlotClock(Mcu, _oid),
                _mcu.ClockSync.GetClockDuration(_queryTime),
                _retransmitCount,
                _invert),
                onInit: true);

            Mcu.RegisterResponseHandler(
                null,
                Mcu.LookupCommand("buttons_state oid=%c ack_count=%c state=%*s"),
                OnButtonsState);

            return ValueTask.CompletedTask;
        }

        private ValueTask OnButtonsState(Exception? exception, McuCommand? command, CancellationToken cancel)
        {
            if (command != null)
            {
                // Expand the message ack_count from 8-bit
                var ackCount = _ackCount;
                var ackDiff = (ackCount - command["ack_count"].Int32) & 0xff;
                if ((ackDiff & 0x80) != 0)
                    ackDiff -= 0x100;
                var msgAckCount = ackCount - ackDiff;
                // Determine new buttons
                var buttons = command["state"].Buffer;
                var newCount = msgAckCount + buttons.Count - _ackCount;
                if (newCount > 0)
                {
                    var newButtons = buttons[^newCount..];
                    // Send ack to MCU
                    _ackCmd.Count.Value = newCount;
                    _ackResult = _mcu.Send(_ackCmd.Cmd, McuCommandPriority.Default, McuOccasion.Now, _ackResult);
                    _ackCount += newCount;
                    // invoke events
                    foreach (var button_ in newButtons)
                    {
                        var button = button_;
                        _buttonEventQueue.EnqueueValue(() => HandleButton(button), null);
                    }
                }
            }
            return ValueTask.CompletedTask;
        }

        private ValueTask HandleButton(int button)
        {
            button ^= _invert;
            var changed = button ^ _lastButton;
            if ((changed & _mask) != 0)
            {
                _lastButton = button;
                var value = button & _mask;
                return ButtonEvent.Invoke(value, _mcu.Manager.RunningCancel);
            }
            else
                return ValueTask.CompletedTask;
        }

        public override string ToString()
            => $"{_name} ({string.Join("; ", _pins.Select(x => x.Key))})";
    }
}
