// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Pins.Tmc2208
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
        [TmcRegisterEnum(typeof(IOIN_TMC222x_SELA0), typeof(IOIN_TMC220x_SELA1))]
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
        PWM_AUTO = 0x72
    }
    public enum GCONF : uint
    {
        I_scale_analog = 0x01,
        internal_Rsense = 0x01 << 1,
        en_spreadCycle = 0x01 << 2,
        shaft = 0x01 << 3,
        index_otpw = 0x01 << 4,
        index_step = 0x01 << 5,
        pdn_disable = 0x01 << 6,
        mstep_reg_select = 0x01 << 7,
        multistep_filt = 0x01 << 8,
        test_mode = 0x01 << 9
    }
    public enum GSTAT : uint
    {
        reset = 0x01,
        drv_err = 0x01 << 1,
        uv_cp = 0x01 << 2
    }
    public enum IFCNT : uint
    {
        IFCNT = 0xff
    }
    public enum SLAVECONF : uint
    {
        SENDDELAY = 0x0f << 8
    }
    public enum OTP_PROG : uint
    {
        OTPBIT = 0x07,
        OTPBYTE = 0x03 << 4,
        OTPMAGIC = 0xff << 8
    }
    public enum OTP_READ : uint
    {
        OTP_FCLKTRIM = 0x1f,
        otp_OTTRIM = 0x01 << 5,
        otp_internalRsense = 0x01 << 6,
        otp_TBL = 0x01 << 7,
        OTP_PWM_GRAD = 0x0f << 8,
        otp_pwm_autograd = 0x01 << 12,
        OTP_TPWMTHRS = 0x07 << 13,
        otp_PWM_OFS = 0x01 << 16,
        otp_PWM_REG = 0x01 << 17,
        otp_PWM_FREQ = 0x01 << 18,
        OTP_IHOLDDELAY = 0x03 << 19,
        OTP_IHOLD = 0x03 << 21,
        otp_en_spreadCycle = 0x01 << 23
    }
    // IOIN mapping depends on the driver type (SEL_A field)
    // TMC222x (SEL_A == 0)
    public enum IOIN_TMC222x_SELA0 : uint
    {
        PDN_UART = 0x01 << 1,
        SPREAD = 0x01 << 2,
        DIR = 0x01 << 3,
        ENN = 0x01 << 4,
        STEP = 0x01 << 5,
        MS1 = 0x01 << 6,
        MS2 = 0x01 << 7,
        SEL_A = 0x01 << 8,
        VERSION = 0xffU << 24
    }
    // TMC220x (SEL_A == 1)
    public enum IOIN_TMC220x_SELA1 : uint
    {
        ENN = 0x01,
        MS1 = 0x01 << 2,
        MS2 = 0x01 << 3,
        DIAG = 0x01 << 4,
        PDN_UART = 0x01 << 6,
        STEP = 0x01 << 7,
        SEL_A = 0x01 << 8,
        DIR = 0x01 << 9,
        VERSION = 0xffU << 24,
    }
    public enum FACTORY_CONF : uint
    {
        FCLKTRIM = 0x1f,
        OTTRIM = 0x03 << 8
    }
    public enum IHOLD_IRUN : uint
    {
        IHOLD = 0x1f,
        IRUN = 0x1f << 8,
        IHOLDDELAY = 0x0f << 16
    }
    public enum TPOWERDOWN : uint
    {
        TPOWERDOWN = 0xff
    }
    public enum TSTEP : uint
    {
        TSTEP = 0xfffff
    }
    public enum TPWMTHRS : uint
    {
        TPWMTHRS = 0xfffff
    }
    public enum VACTUAL : uint
    {
        VACTUAL = 0xffffff
    }
    public enum MSCNT : uint
    {
        MSCNT = 0x3ff
    }
    public enum MSCURACT : uint
    {
        [TmcSignedField]
        CUR_A = 0x1ff,
        [TmcSignedField]
        CUR_B = 0x1ff << 16
    }
    public enum CHOPCONF : uint
    {
        toff = 0x0f,
        hstrt = 0x07 << 4,
        hend = 0x0f << 7,
        TBL = 0x03 << 15,
        vsense = 0x01 << 17,
        MRES = 0x0f << 24,
        intpol = 0x01 << 28,
        dedge = 0x01 << 29,
        diss2g = 0x01 << 30,
        diss2vs = 0x01U << 31
    }
    public enum DRV_STATUS : uint
    {
        otpw = 0x01,
        ot = 0x01 << 1,
        s2ga = 0x01 << 2,
        s2gb = 0x01 << 3,
        s2vsa = 0x01 << 4,
        s2vsb = 0x01 << 5,
        ola = 0x01 << 6,
        olb = 0x01 << 7,
        t120 = 0x01 << 8,
        t143 = 0x01 << 9,
        t150 = 0x01 << 10,
        t157 = 0x01 << 11,
        CS_ACTUAL = 0x1f << 16,
        stealth = 0x01 << 30,
        stst = 0x01U << 31
    }
    public enum PWMCONF : uint
    {
        PWM_OFS = 0xff,
        PWM_GRAD = 0xff << 8,
        pwm_freq = 0x03 << 16,
        pwm_autoscale = 0x01 << 18,
        pwm_autograd = 0x01 << 19,
        freewheel = 0x03 << 20,
        PWM_REG = 0xf << 24,
        PWM_LIM = 0xfU << 28
    }
    public enum PWM_SCALE : uint
    {
        PWM_SCALE_SUM = 0xff,
        [TmcSignedField]
        PWM_SCALE_AUTO = 0x1ff << 16
    }
    public enum PWM_AUTO : uint
    {
        PWM_OFS_AUTO = 0xff,
        PWM_GRAD_AUTO = 0xff << 16
    }
}
