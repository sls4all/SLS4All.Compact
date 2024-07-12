// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Threading;

namespace SLS4All.Compact.Printer
{
    public record class PrinterPerformanceValues(
        float SelfCpuLoad,
        float TotalCpuLoad,
        float TotalIOLoad,
        long SelfUsedMemory,
        long TotalUsedMemory,
        long TotalAvaialableMemory,
        float? CpuTemperature,
        float? GpuTemperature,
        long StorageUsed,
        long StorageTotal,
        int ThreadPoolThreadCount,
        SystemTimestamp SystemStartTimestamp,
        SystemTimestamp ApplicationStartTimestamp
    )
    {
        public float TotalMemoryLoad => TotalAvaialableMemory > 0 ? (float)TotalUsedMemory / TotalAvaialableMemory * 100 : 0;
        public float SelfMemoryLoad => TotalAvaialableMemory > 0 ? (float)SelfUsedMemory / TotalAvaialableMemory * 100 : 0;
        public float StorageLoad => StorageTotal > 0 ? (float)StorageUsed / StorageTotal * 100 : 0;
    }

    public interface IPrinterPerformanceProvider
    {
        AsyncEvent<PrinterPerformanceValues> ValuesChangedEvent { get; }
        PrinterPerformanceValues Values { get; }
    }
}