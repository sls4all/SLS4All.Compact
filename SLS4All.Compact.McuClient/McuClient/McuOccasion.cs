// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public readonly record struct McuOccasion
    {
        private readonly long _minClockRaw;
        private readonly long _reqClock;
        private readonly long _maxClock;

        public long MinClock => _minClockRaw >= 0 ? _minClockRaw : ~_minClockRaw;
        public long ReqClock => _reqClock;
        public long MaxClock => _maxClock;
        public bool IgnoreLate => _minClockRaw < 0;

        public static McuOccasion Now => new McuOccasion(0, 0, 0, ignoreLate: true);

        public McuOccasion(long minClock, long reqClock, long maxClock = long.MinValue, bool ignoreLate = false)
        {
            minClock = Math.Max(minClock, 0);
            reqClock = Math.Max(reqClock, 0);
            _minClockRaw = ignoreLate ? minClock : ~minClock;
            _reqClock = reqClock;
            _maxClock = maxClock != long.MinValue ? maxClock : reqClock;
            Debug.Assert(_maxClock >= _reqClock);
        }
    }
}
