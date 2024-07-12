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

namespace SLS4All.Compact.Diagnostics
{
    public readonly record struct SystemTimestamp(long Timestamp)
    {
        private static readonly double s_tickFrequency = (double)TimeSpan.TicksPerSecond / Stopwatch.Frequency;
        private static readonly double s_tickFrequencyInv = (double)Stopwatch.Frequency / TimeSpan.TicksPerSecond;

        public bool IsEmpty => Timestamp == 0;
        public static DateTime BaseDateTimeValue { get; set; }
        public static long BaseTimestampValue { get; } 
        public static SystemTimestamp Now => new SystemTimestamp(Stopwatch.GetTimestamp());
        public double TotalSeconds => (double)Timestamp / Stopwatch.Frequency;
        public TimeSpan ElapsedFromNow => Stopwatch.GetElapsedTime(Timestamp);

        static SystemTimestamp()
        {
            BaseDateTimeValue = DateTime.Now;
            BaseTimestampValue = Stopwatch.GetTimestamp();
        }

        public static long ToTicks(TimeSpan duration)
            => (long)(duration.Ticks * s_tickFrequencyInv);

        public static SystemTimestamp FromTotalSeconds(double value)
            => new SystemTimestamp((long)(value * Stopwatch.Frequency));

        public static SystemTimestamp operator +(SystemTimestamp timestamp, double seconds)
            => new SystemTimestamp(timestamp.Timestamp + (long)(seconds * Stopwatch.Frequency));

        public static SystemTimestamp operator -(SystemTimestamp timestamp, double seconds)
            => new SystemTimestamp(timestamp.Timestamp - (long)(seconds * Stopwatch.Frequency));

        public static SystemTimestamp operator +(SystemTimestamp timestamp, TimeSpan span)
            => new SystemTimestamp(timestamp.Timestamp + (long)(span.Ticks * s_tickFrequencyInv));

        public static SystemTimestamp operator -(SystemTimestamp timestamp, TimeSpan span)
            => new SystemTimestamp(timestamp.Timestamp - (long)(span.Ticks * s_tickFrequencyInv));

        public static TimeSpan operator -(SystemTimestamp first, SystemTimestamp second)
            => new TimeSpan((long)((first.Timestamp - second.Timestamp) * s_tickFrequency));

        public static bool operator <(SystemTimestamp first, SystemTimestamp second)
            => first.Timestamp < second.Timestamp;
        public static bool operator <=(SystemTimestamp first, SystemTimestamp second)
            => first.Timestamp <= second.Timestamp;
        public static bool operator >(SystemTimestamp first, SystemTimestamp second)
            => first.Timestamp > second.Timestamp;
        public static bool operator >=(SystemTimestamp first, SystemTimestamp second)
            => first.Timestamp >= second.Timestamp;

        public DateTime ToDateTimeUtc() => BaseDateTimeValue.AddSeconds(TotalSeconds);

        public override string ToString()
            => IsEmpty ? "empty" : $"{TotalSeconds:0.000000} [{ToDateTimeUtc()}]";
    }
}
