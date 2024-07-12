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

namespace SLS4All.Compact.McuClient
{
    public static class McuHelpers
    {
        public static long GetQuerySlotClock(this IMcu mcu, int oid)
        {
            var now = SystemTimestamp.Now + TimeSpan.FromSeconds(1.5 + oid * 0.010);
            return mcu.ClockSync.GetClock(now);
        }

        public static long Clock32ToClock64(long reference, uint clock)
        {
            var candidate1 = (reference & ~0xffff_ffffL) | clock;
            var candidate2 = candidate1 - 0x1_0000_0000L;
            var candidate3 = candidate1 + 0x1_0000_0000L;
            if (Math.Abs(reference - candidate1) < Math.Abs(reference - candidate2))
            {
                if (Math.Abs(reference - candidate1) < Math.Abs(reference - candidate3))
                    return candidate1;
                else
                    return candidate3;
            }
            else
            {
                if (Math.Abs(reference - candidate2) < Math.Abs(reference - candidate3))
                    return candidate2;
                else
                    return candidate3;
            }
        }
    }
}
