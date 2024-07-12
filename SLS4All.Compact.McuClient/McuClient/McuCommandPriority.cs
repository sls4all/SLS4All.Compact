// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.McuClient
{
    public static class McuCommandPriority
    {
        public const int Printing = 0_200; // NOTE: lowest, generates constant stream of commands
        public const int Default = 0_300;
        public const int ClockSync = 0_800;
        public const int Initialize = 0_900;
        public const int Identify = 1_000;
        public const int Shutdown = 1_100;
    }
}
