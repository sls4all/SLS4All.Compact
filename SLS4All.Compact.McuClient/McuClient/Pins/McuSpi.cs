// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.VisualBasic;
using Nito.AsyncEx;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SLS4All.Compact.McuClient.Pins
{
    public sealed class McuSpi : IMcuSpi
    {
        private readonly int _mode;
        private readonly int _rateInit;
        private readonly McuBusDescription _busDesc;
        private readonly McuPinDescription? _csDesc;
        private int _oid;
        private (McuCommand Cmd, int Oid, int SpiBus, int Mode, int Rate) _setBusCmd = (McuCommand.PlaceholderCommand, 0, 0, 0, 0);
        private (McuCommand Cmd, int Data) _sendCmd = (McuCommand.PlaceholderCommand, 0);
        private (McuCommand Cmd, int Data) _sendContinuousCmd = (McuCommand.PlaceholderCommand, 0);
        private (McuCommand Cmd, int Data) _transferCmd = (McuCommand.PlaceholderCommand, 0);
        private (McuCommand Cmd, int Data) _transferContinuousCmd = (McuCommand.PlaceholderCommand, 0);
        private (McuCommand Cmd, int Oid, int Response) _transferCmdResponse = (McuCommand.PlaceholderCommand, 0, 0);
        private Func<McuCommand, bool> _transferCmdResponseFilter;
        private int _lastPriority;
        private bool _isContinuous;

        public IMcu Mcu => _busDesc.Mcu;
        public int Oid => _oid;

        public McuSpi(McuBusDescription busDesc, McuPinDescription? csDesc, int mode, int rate)
        {
            if (csDesc != null && busDesc.Mcu != csDesc.Mcu)
                throw new ArgumentException($"Bus ({busDesc}), and CS ({csDesc}) must be on the same MCU");
            _busDesc = busDesc;
            _csDesc = csDesc;
            _mode = mode;
            _rateInit = rate;
            _transferCmdResponseFilter = default!;

            Mcu.RegisterConfigCommand(BuildConfig);
        }

        public async Task Continuous(Func<CancellationToken, Task> func, CancellationToken cancel)
        {
            var locker = _busDesc.Mcu.GetLock(_busDesc.Key);
            using (await locker.LockAsync(cancel))
            {
                _isContinuous = true;
                try
                {
                    await func(cancel);
                }
                finally
                {
                    _isContinuous = false;
                    Send(default, _lastPriority, McuOccasion.Now);
                }
            }
        }

        public void Send(ArraySegment<byte> data, int priority, McuOccasion clock)
        {
            _lastPriority = priority;
            var cmd = _isContinuous ? _sendContinuousCmd : _sendCmd;
            lock (cmd.Cmd)
            {
                cmd.Cmd[cmd.Data] = new McuCommandArgumentValue(0, data, default);
                Mcu.Send(cmd.Cmd, priority, clock);
            }
        }

        public async Task<ArraySegment<byte>> Transfer(ArraySegment<byte> data, int priority, McuOccasion clock, CancellationToken cancel)
        {
            Task<McuCommand> responseTask;
            _lastPriority = priority;
            var cmd = _isContinuous ? _transferContinuousCmd : _transferCmd;
            lock (cmd.Cmd)
            {
                cmd.Cmd[cmd.Data] = data;
                responseTask = Mcu.SendWithResponse(cmd.Cmd, _transferCmdResponse.Cmd, _transferCmdResponseFilter, priority, clock, cancel: cancel);
            }
            var response = await responseTask;
            return response[_transferCmdResponse.Response].Buffer;
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            _oid = commands.CreateOid();

            _setBusCmd = Mcu.LookupCommand("spi_set_bus oid=%c spi_bus=%u mode=%u rate=%u", "oid", "spi_bus", "mode", "rate").Bind(
                _oid,
                Mcu.Config.GetBus(_busDesc.Bus),
                _mode,
                _rateInit);
            _sendCmd = Mcu.LookupCommand("spi_send oid=%c data=%*s", "data")
                .Bind("oid", _oid);
            _sendContinuousCmd = Mcu.LookupCommand("spi_send_continuous oid=%c data=%*s", "data")
                .Bind("oid", _oid);
            _transferCmd = Mcu.LookupCommand("spi_transfer oid=%c data=%*s", "data")
                .Bind("oid", _oid);
            _transferContinuousCmd = Mcu.LookupCommand("spi_transfer_continuous oid=%c data=%*s", "data")
                .Bind("oid", _oid);
            _transferCmdResponse = Mcu.LookupCommand("spi_transfer_response oid=%c response=%*s", "oid", "response")
                .Bind("oid", _oid);
            _transferCmdResponseFilter = TransferCmdResponseFilter;

            if (_csDesc != null)
                commands.Add(Mcu.LookupCommand("config_spi oid=%c pin=%u").Bind(
                    _oid,
                    Mcu.Config.GetPin(_csDesc.Pin)));
            else
                commands.Add(Mcu.LookupCommand("config_spi_without_cs oid=%c").Bind(
                    _oid));
            commands.Add(_setBusCmd.Cmd);
            return ValueTask.CompletedTask;
        }

        public Task SetRate(int rate, CancellationToken cancel)
        {
            Task task;
            lock (_setBusCmd.Cmd)
            {
                _setBusCmd.Cmd[_setBusCmd.Rate] = rate;
                task = Mcu.SendWait(_setBusCmd.Cmd, McuCommandPriority.Default, McuOccasion.Now, cancel);
            }
            return task;
        }

        private bool TransferCmdResponseFilter(McuCommand cmd)
            => cmd[_transferCmdResponse.Oid].Int32 == _oid;
    }
}
