// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.McuClient.Pins.Tmc2209;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Pins
{
    public class Tmc220xDriverOptions
    {
        public int Retries { get; set; } = 5;
        public required string UartPin { get; set; }
        public string? TxPin { get; set; }
        public int UartAddress { get; set; } = 0;
        public required float RunCurrent { get; set; }
        public required float HoldCurrent { get; set; }
        public float SenseResistor { get; set; } = 0.110f;
        public required int Microsteps { get; set; }
        public bool Interpolate { get; set; } = true;
        public Dictionary<string, long?> Fields { get; } = new();
    }

    public abstract class Tmc220xDriverBase<TRegister> : IStepperDriver
        where TRegister : struct, Enum
    {
        private readonly IOptions<Tmc220xDriverOptions> _options;
        private readonly McuManager _manager;
        private readonly TmcUart _uart;
        private readonly TmcFieldCollection<TRegister> _fields;
        private readonly AsyncLock _lock = new();
        private long? _ifcnt;

        public IMcu Mcu => _uart.Mcu;
        public int Microsteps => _options.Value.Microsteps;
        protected TmcFieldCollection<TRegister> Fields => _fields;

        public Tmc220xDriverBase(
            IOptions<Tmc220xDriverOptions> options,
            McuManager manager)
        {
            _options = options;
            _manager = manager;

            var o = options.Value;
            var rx = _manager.ClaimPin(McuPinType.Digital, o.UartPin, canPullup: true, shareType: "tmc_uart_rx");
            var tx = !string.IsNullOrEmpty(o.TxPin)
                ? _manager.ClaimPin(McuPinType.Digital, o.TxPin, shareType: "tmc_uart_tx")
                : rx;
            _uart = new TmcUart(rx, tx, null);
            _fields = new TmcFieldCollection<TRegister>();

            var current = CalcCurrent(o.RunCurrent, o.HoldCurrent);
            SetInitialField("vsense", current.vsense ? 1 : 0);
            SetInitialField("IHOLD", current.ihold);
            SetInitialField("IRUN", current.irun);

            var steps = o.Microsteps switch
            {
                256 => 0,
                128 => 1,
                64 => 2,
                32 => 3,
                16 => 4,
                8 => 5,
                4 => 6,
                2 => 7,
                1 => 8,
                _ => throw new InvalidOperationException($"Invalid microsteps value {o.Microsteps}")
            };
            SetInitialField("MRES", steps);
            SetInitialField("intpol", o.Interpolate ? 1 : 0);

            manager.RegisterSetup(Mcu, OnSetup);
        }

        protected void SetInitialField(string field, long defaultFieldValue)
        {
            var options = _options.Value;
            _fields.SetField(field, defaultFieldValue, options.Fields);
        }

        private async ValueTask OnSetup(CancellationToken cancel)
        {
            await InitRegisters(cancel);
        }

        private async Task InitRegisters(CancellationToken cancel)
        {
            foreach (var field in _fields.Values)
                await SetRegister(field.Reg, field.Value, cancel);
        }

        public async Task SetRegister(TRegister reg, uint value, CancellationToken cancel)
        {
            var options = _options.Value;
            var ifcntReg = TmcFields<TRegister>.NameToRegister["IFCNT"];
            var regNum = Convert.ToInt32(reg);
            using (await _lock.LockAsync(cancel))
            {
                if (await GetRegisterInner(reg, cancel) == value)
                    return;
                for (int i = 0; i < 5; i++)
                {
                    var ifcnt = _ifcnt;
                    if (ifcnt == null)
                        _ifcnt = ifcnt = await GetRegisterInner(ifcntReg, cancel);
                    await _uart.WriteRegister(options.UartAddress, regNum, value, null, cancel);
                    _ifcnt = await GetRegisterInner(ifcntReg, cancel);
                    if (_ifcnt == ((ifcnt + 1) & 0xff))
                        return;
                }
            }
            throw new TimeoutException($"Failed to write register {reg} value {value} after multiple tries");
        }

        public async Task<long> GetRegister(TRegister reg, CancellationToken cancel)
        {
            using (await _lock.LockAsync(cancel))
            {
                return await GetRegisterInner(reg, cancel);
            }
        }

        private async Task<long> GetRegisterInner(TRegister reg, CancellationToken cancel)
        {
            var options = _options.Value;
            var regNum = Convert.ToInt32(reg);
            for (int i = 0; i < options.Retries; i++)
            {
                var res = await _uart.TryReadRegister(options.UartAddress, regNum, cancel);
                if (res != null)
                    return res.Value;
            }
            throw new TimeoutException($"Failed to read register {reg} after multiple tries");
        }

        public int CalcCurrentBits(float current, bool vsense)
        {
            var options = _options.Value;
            var senseResistor = options.SenseResistor + 0.020f;
            var vref = 0.32f;
            if (vsense)
                vref = 0.18f;
            var cs = (int)(32.0f * current * senseResistor * MathF.Sqrt(2) / vref - 1.0 + 0.5);
            return Math.Max(0, Math.Min(31, cs));
        }

        public (bool vsense, int irun, int ihold) CalcCurrent(float runCurrent, float holdCurrent)
        {
            var vsense = false;
            var irun = CalcCurrentBits(runCurrent, vsense);
            var ihold = CalcCurrentBits(Math.Min(holdCurrent, runCurrent), vsense);
            if (irun < 16 && ihold < 16)
            {
                vsense = true;
                irun = CalcCurrentBits(runCurrent, vsense);
                ihold = CalcCurrentBits(Math.Min(holdCurrent, runCurrent), vsense);
            }
            return (vsense, irun, ihold);
        }

        public abstract ValueTask BeginHomingMove(CancellationToken cancel);
        public abstract ValueTask EndHomingMove(CancellationToken cancel);
    }
}
