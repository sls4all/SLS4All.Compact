// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;

namespace SLS4All.Compact.McuClient
{
    public interface IMcuClockSync
    {
        bool IsReady { get; }
        CancellationToken UnreachableCancel { get; }
        WaitHandle UpdatedEvent { get; }
        long UpdatedCount { get; }

        double GetSecondsDuration(long clock);
        double GetSecondsDurationDouble(double clock);
        TimeSpan GetSpanDuration(long clock);

        long GetClockDuration(TimeSpan duration);
        long GetClockDuration(double seconds);
        double GetClockDurationDouble(double seconds);

        long GetClock(SystemTimestamp timestamp);
        double GetClockDouble(SystemTimestamp timestamp);

        SystemTimestamp GetTimestamp(long clock);
        SystemTimestamp GetTimestampDouble(double clock);

        Task Start(IMcu mcu, TaskScheduler scheduler, CancellationToken cancel);
        void Stop();
    }
}