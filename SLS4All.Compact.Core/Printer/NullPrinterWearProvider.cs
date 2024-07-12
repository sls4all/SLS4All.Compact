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

namespace SLS4All.Compact.Printer
{
    public sealed class NullPrinterWearProvider : IPrinterWearProvider
    {
        public static NullPrinterWearProvider Instance { get; } = new();

        public DateTimeOffset FirstUpdateTime => DateTimeOffset.UtcNow;

        public TimeSpan HalogenDuration => TimeSpan.Zero;

        public TimeSpan LaserDuration => TimeSpan.Zero;

        public double RDistance => 0;

        public double Z1Distance => 0;

        public double Z2Distance => 0;
    }
}
