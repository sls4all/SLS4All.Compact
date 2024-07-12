// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.VisualBasic;
using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Pins
{
    public sealed class TmcUart
    {
        private const int _sync = 0xf5;
        private const double _bitSeconds = 1.0 / 40000;
        private readonly McuPinDescription _rxDesc;
        private readonly McuPinDescription _txDesc;
        private int _oid;
        private McuCommand _sendCmd = McuCommand.PlaceholderCommand;
        private McuCommand _sendCmdResponse = McuCommand.PlaceholderCommand;

        public IMcu Mcu => _rxDesc.Mcu;

        public TmcUart(McuPinDescription rxDesc, McuPinDescription txDesc, McuPinDescription? selDesc)
        {
            if (rxDesc.Mcu != txDesc.Mcu || (selDesc != null && selDesc.Mcu != rxDesc.Mcu))
                throw new ArgumentException($"RX ({rxDesc}), TX ({txDesc}) and SELECT ({selDesc}) pins must be on the same MCU");
            if (selDesc != null)
                throw new NotSupportedException("SELECT pin is not supported");
            _rxDesc = rxDesc;
            _txDesc = txDesc;

            Mcu.RegisterConfigCommand(BuildConfig);
        }

        /// <summary>
        /// Generate a CRC8-ATM value
        /// </summary>
        private static byte CalcCrc8(Span<byte> data)
        {
            byte crc = 0;
            for (int q = 0; q < data.Length; q++)
            {
                var b = data[q];
                for (int i = 0; i < 8; i++)
                {
                    if (((crc >> 7) ^ (b & 0x01)) != 0)
                        crc = (byte)((crc << 1) ^ 0x07);
                    else
                        crc = (byte)(crc << 1);
                    b >>= 1;
                }
            }
            return crc;
        }

        /// <summary>
        /// Add serial start and stop bits to a message
        /// </summary>
        private static byte[] AddSerialBits(Span<byte> data)
        {
            var num = new BigInteger(0);
            var pos = 0;
            for (int i = 0; i < data.Length; i++)
            {
                var d = data[i];
                var b = (d << 1) | 0x200;
                num |= new BigInteger(b) << pos;
                pos += 10;
            }
            var res = num.ToByteArray(true, false);
            return res;
        }

        private static byte[] EncodeRead(int sync, int addr, int reg)
        {
            var data = new List<byte> { (byte)sync, (byte)addr, (byte)reg };
            data.Add(CalcCrc8(CollectionsMarshal.AsSpan(data)));
            return AddSerialBits(CollectionsMarshal.AsSpan(data));
        }

        private static byte[] EncodeWrite(int sync, int addr, int reg, uint val)
        {
            var data = new List<byte> { (byte)sync, (byte)addr, (byte)reg, (byte)(val >> 24), (byte)(val >> 16), (byte)(val >> 8), (byte)val };
            data.Add(CalcCrc8(CollectionsMarshal.AsSpan(data)));
            return AddSerialBits(CollectionsMarshal.AsSpan(data));
        }

        public async Task<uint?> TryReadRegister(int addr, int reg, CancellationToken cancel)
        {
            Task<McuCommand> task;
            lock (_sendCmd)
            {
                _sendCmd
                    .Bind("write", EncodeRead(_sync, addr, reg))
                    .Bind("read", 10);
                task = Mcu.SendWithResponse(
                    _sendCmd,
                    _sendCmdResponse,
                    response => response["oid"].Int32 == _oid,
                    McuCommandPriority.Default,
                    McuOccasion.Now,
                    cancel: cancel);
            }
            try
            {
                var res = await task;
                return TryDecodeRead(reg, res["read"].Buffer);
            }
            catch (TimeoutException)
            {
                return null;
            }
        }

        public async Task WriteRegister(int addr, int reg, uint value, SystemTimestamp? timestamp, CancellationToken cancel)
        {
            McuOccasion occasion;
            if (timestamp != null)
            {
                var clock = Mcu.ClockSync.GetClock(timestamp.Value);
                occasion = new McuOccasion(clock, clock);
            }
            else
                occasion = McuOccasion.Now;
            Task<McuCommand> task;
            lock (_sendCmd)
            {
                _sendCmd
                    .Bind("write", EncodeWrite(_sync, addr, reg | 0x80, value))
                    .Bind("read", 0);
                task = Mcu.SendWithResponse(
                    _sendCmd,
                    _sendCmdResponse,
                    response => response["oid"].Int32 == _oid,
                    McuCommandPriority.Default,
                    occasion,
                    cancel: cancel);
            }
            await task;
        }

        /// <summary>
        /// Extract a uart read response message
        /// </summary>
        private static uint? TryDecodeRead(int reg, Span<byte> data)
        {
            if (data.Length != 10)
                return null;
            // Convert data into a long integer for easy manipulation
            var mval = new BigInteger(data, true, false);
            // Extract register value
            var val = ((((mval >> 31) & 0xff) << 24) | (((mval >> 41) & 0xff) << 16)
               | (((mval >> 51) & 0xff) << 8) | ((mval >> 61) & 0xff));
            var res = (uint)val;
            // Verify start/stop bits and crc
            var encodedData = EncodeWrite(0x05, 0xff, reg, res);
            if (!encodedData.AsSpan().SequenceEqual(data))
                return null;
            return res;
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            _oid = commands.CreateOid();
            var bitTicks = _rxDesc.Mcu.ClockSync.GetClockDuration(_bitSeconds);
            commands.Add(Mcu.LookupCommand("config_tmcuart oid=%c rx_pin=%u pull_up=%c tx_pin=%u bit_time=%u").Bind(
                _oid,
                Mcu.Config.GetPin(_rxDesc.Pin),
                _rxDesc.Pullup,
                Mcu.Config.GetPin(_txDesc.Pin),
                (int)bitTicks));
            _sendCmd = Mcu.LookupCommand("tmcuart_send oid=%c write=%*s read=%c")
                .Bind("oid", _oid);
            _sendCmdResponse = Mcu.LookupCommand("tmcuart_response oid=%c read=%*s");
            return ValueTask.CompletedTask;
        }
    }
}
