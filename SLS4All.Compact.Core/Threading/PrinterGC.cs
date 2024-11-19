// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Printer;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public static class PrinterGC
    {
        [DllImport("libc", SetLastError = true, EntryPoint = "mlockall")]
        private static extern int mlockall(int flags);

        /// <summary>
        /// lock all current mappings 
        /// </summary>
        private const int MCL_CURRENT = 1;
        /// <summary>
        /// lock all future mapping
        /// </summary>
        private const int MCL_FUTURE = 2;

        public static bool TryDisableProcessPaging()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return mlockall(MCL_FUTURE | MCL_CURRENT) == 0;
            }
            else
                return true;
        }

        public static void CollectGarbageBlockingAggressive()
        {
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
        }

        public static void CollectGarbageBlocking()
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
        }

        public static void LogCollectionCount(ILogger logger)
        {
            var maxGeneration = GC.MaxGeneration;
            var gc0 = maxGeneration >= 0 ? GC.CollectionCount(0) : 0;
            var gc1 = maxGeneration >= 1 ? GC.CollectionCount(1) : 0;
            var gc2 = maxGeneration >= 2 ? GC.CollectionCount(2) : 0;
            var gcLatencyMode = GCSettings.LatencyMode;
            logger.LogDebug($"Garbage collection count: GC(0)={gc0}, GC(1)={gc1}, GC(2)={gc2}, LatencyMode={gcLatencyMode}");
        }
    }
}
