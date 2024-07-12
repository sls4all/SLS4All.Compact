// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using SLS4All.Compact.McuClient.Pins.Tmc2208;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Pins
{
    public class Tmc2209Driver : Tmc220xDriverBase<Tmc2209.Registers>
    {
        private readonly IOptions<Tmc220xDriverOptions> _options;

        public Tmc2209Driver(
            IOptions<Tmc220xDriverOptions> options,
            McuManager manager)
            : base(options, manager)
        {
            _options = options;
            SetInitialField("pdn_disable", 1);
            SetInitialField("mstep_reg_select", 1);
            SetInitialField("SENDDELAY", 2);
            SetInitialField("multistep_filt", 1);
            SetInitialField("toff", 3);
            SetInitialField("hstrt", 5);
            SetInitialField("hend", 0);
            SetInitialField("TBL", 2);
            SetInitialField("IHOLDDELAY", 8);
            SetInitialField("TPOWERDOWN", 20);
            SetInitialField("PWM_OFS", 36);
            SetInitialField("PWM_GRAD", 14);
            SetInitialField("pwm_freq", 1);
            SetInitialField("pwm_autoscale", 1);
            SetInitialField("pwm_autograd", 1);
            SetInitialField("PWM_REG", 8);
            SetInitialField("PWM_LIM", 12);
            SetInitialField("TPOWERDOWN", 20);
            SetInitialField("SGTHRS", 0);
            SetInitialField("TPWMTHRS", 0); // enable stealthchop, TODO: configurable?
            SetInitialField("en_spreadCycle", 0); // disable spreadcycle, TODO: configurable?
            SetInitialField("TCOOLTHRS", 0); // disable spreadcycle, TODO: configurable?
        }

        public override async ValueTask BeginHomingMove(CancellationToken cancel)
        {
            await SetRegister(Tmc2209.Registers.TCOOLTHRS, 0xfffff, cancel);
        }

        public override async ValueTask EndHomingMove(CancellationToken cancel)
        {
            await SetRegister(Tmc2209.Registers.TCOOLTHRS, 0, cancel);
        }

        public override string ToString()
        {
            var options = _options.Value;
            return $"Tmc2209Driver {{UartPin = {options.UartPin}, UartAddress = {options.UartAddress}}}";
        }
    }
}
