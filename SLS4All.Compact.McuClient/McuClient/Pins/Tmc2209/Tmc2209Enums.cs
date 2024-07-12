// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.McuClient.Pins.Tmc2208;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Pins.Tmc2209
{
    public enum Registers
    {
        [TmcRegisterEnum(typeof(GCONF))]
        GCONF = 0x00,
        [TmcRegisterEnum(typeof(GSTAT))]
        GSTAT = 0x01,
        [TmcRegisterEnum(typeof(IFCNT))]
        IFCNT = 0x02,
        [TmcRegisterEnum(typeof(SLAVECONF))]
        SLAVECONF = 0x03,
        [TmcRegisterEnum(typeof(OTP_PROG))]
        OTP_PROG = 0x04,
        [TmcRegisterEnum(typeof(OTP_READ))]
        OTP_READ = 0x05,
        [TmcRegisterEnum(typeof(IOIN))]
        IOIN = 0x06,
        [TmcRegisterEnum(typeof(FACTORY_CONF))]
        FACTORY_CONF = 0x07,
        [TmcRegisterEnum(typeof(IHOLD_IRUN))]
        IHOLD_IRUN = 0x10,
        [TmcRegisterEnum(typeof(TPOWERDOWN))]
        TPOWERDOWN = 0x11,
        [TmcRegisterEnum(typeof(TSTEP))]
        TSTEP = 0x12,
        [TmcRegisterEnum(typeof(TPWMTHRS))]
        TPWMTHRS = 0x13,
        [TmcRegisterEnum(typeof(VACTUAL))]
        VACTUAL = 0x22,
        [TmcRegisterEnum(typeof(MSCNT))]
        MSCNT = 0x6a,
        [TmcRegisterEnum(typeof(MSCURACT))]
        MSCURACT = 0x6b,
        [TmcRegisterEnum(typeof(CHOPCONF))]
        CHOPCONF = 0x6c,
        [TmcRegisterEnum(typeof(DRV_STATUS))]
        DRV_STATUS = 0x6f,
        [TmcRegisterEnum(typeof(PWMCONF))]
        PWMCONF = 0x70,
        [TmcRegisterEnum(typeof(PWM_SCALE))]
        PWM_SCALE = 0x71,
        [TmcRegisterEnum(typeof(PWM_AUTO))]
        PWM_AUTO = 0x72,
        [TmcRegisterEnum(typeof(TCOOLTHRS))]
        TCOOLTHRS = 0x14,
        [TmcRegisterEnum(typeof(COOLCONF))]
        COOLCONF = 0x42,
        [TmcRegisterEnum(typeof(SGTHRS))]
        SGTHRS = 0x40,
        [TmcRegisterEnum(typeof(SG_RESULT))]
        SG_RESULT = 0x41
    }
    public enum COOLCONF : uint
    {
        semin = 0x0F << 0,
        seup = 0x03 << 5,
        semax = 0x0F << 8,
        sedn = 0x03 << 13,
        seimin = 0x01 << 15
    }
    public enum IOIN : uint
    {
        ENN = 0x01 << 0,
        MS1 = 0x01 << 2,
        MS2 = 0x01 << 3,
        DIAG = 0x01 << 4,
        PDN_UART = 0x01 << 6,
        STEP = 0x01 << 7,
        SPREAD_EN = 0x01 << 8,
        DIR = 0x01 << 9,
        VERSION = 0xffU << 24
    }
    public enum SGTHRS : uint
    {
        SGTHRS = 0xFF << 0
    }
    public enum SG_RESULT : uint
    {
        SG_RESULT = 0x3FF << 0
    }
    public enum TCOOLTHRS : uint
    {
        TCOOLTHRS = 0xfffff
    }
}
