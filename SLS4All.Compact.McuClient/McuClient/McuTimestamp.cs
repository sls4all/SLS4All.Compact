// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public readonly record struct McuTimestamp
    {
        private readonly long _clockPrecise;
        private readonly int _precision;

        public IMcu? Mcu { get; }
        public long Clock => _clockPrecise >> _precision;
        public double ClockDouble => _precision == 0 ? _clockPrecise : (double)_clockPrecise / (1L << _precision);
        public long ClockPrecise => _clockPrecise;
        public int Precision => _precision;
        public bool IsEmpty => Mcu == null;
        public bool IsImmediate => Mcu != null && Clock == 0;

        public McuTimestamp(IMcu? mcu, long clock)
        {
            Mcu = mcu;
            _clockPrecise = clock;
            _precision = 0;
        }

        public McuTimestamp(IMcu? mcu, long clockPrecise, int precision)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(precision, 0);
            Mcu = mcu;
            _clockPrecise = clockPrecise;
            _precision = precision;
        }

        public McuTimestamp WithPrecision(int precision)
        {
            var shift = _precision - precision;
            if (shift > 0)
                return new McuTimestamp(Mcu, _clockPrecise >> shift, precision);
            else
                return new McuTimestamp(Mcu, _clockPrecise << shift, precision);
        }

        public long GetClockWithPrecision(int precision)
        {
            var shift = _precision - precision;
            if (shift > 0)
                return _clockPrecise >> shift;
            else
                return _clockPrecise << shift;
        }

        public override string ToString()
        {
            if (Mcu == null)
                return "empty";
            else
                return $"{Clock} ({ClockPrecise} @ {Precision}) for {Mcu.Name} = {Mcu.ClockSync.GetTimestamp(Clock)}";
        }

        public static McuTimestamp FromSystem(IMcu mcu, SystemTimestamp timestamp, int precision = 0)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(precision, 0);
            if (precision == 0)
                return new McuTimestamp(mcu, mcu.ClockSync.GetClock(timestamp));
            else
                return new McuTimestamp(mcu, (long)(mcu.ClockSync.GetClockDouble(timestamp) * (1L << precision)), precision);
        }

        public SystemTimestamp ToSystem()
        {
            if (IsEmpty)
                throw new InvalidOperationException("McuTimestamp is empty, Mcu is not set");
            if (Clock == 0)
                throw new InvalidOperationException("McuTimestamp.Now cannot be represented in SystemTimestamp");
            return Mcu!.ClockSync.GetTimestampDouble(ClockDouble);
        }

        public double ToRelativeSeconds()
        {
            if (IsEmpty)
                throw new InvalidOperationException("McuTimestamp is empty, Mcu is not set");
            if (Clock == 0)
                throw new InvalidOperationException("McuTimestamp.Now cannot be represented in SystemTimestamp");
            return Mcu!.ClockSync.GetSecondsDurationDouble(ClockDouble);
        }

        public static McuTimestamp Immediate(IMcu mcu)
            => new McuTimestamp(mcu, 0);

        public static McuTimestamp Now(IMcu mcu)
            => FromSystem(mcu, SystemTimestamp.Now);

        public static McuTimestamp operator +(McuTimestamp timestamp, double seconds)
        {
            if (timestamp.Mcu == null)
                throw new InvalidOperationException("McuTimestamp is empty, Mcu is not set");
            if (seconds == 0)
                return timestamp;
            else if (timestamp.Precision == 0)
                return new McuTimestamp(timestamp.Mcu, timestamp.ClockPrecise + timestamp.Mcu.ClockSync.GetClockDuration(seconds));
            else
                return new McuTimestamp(timestamp.Mcu, timestamp.ClockPrecise + (long)(timestamp.Mcu.ClockSync.GetClockDurationDouble(seconds) * (1L << timestamp.Precision)), timestamp.Precision);
        }

        public static McuTimestamp operator -(McuTimestamp timestamp, double seconds)
            => timestamp + (-seconds);

        public static McuTimestamp operator +(McuTimestamp timestamp, TimeSpan span)
            => timestamp + span.TotalSeconds;

        public static McuTimestamp operator -(McuTimestamp timestamp, TimeSpan span)
            => timestamp + (-span.TotalSeconds);

        public static bool operator <(McuTimestamp left, McuTimestamp right)
            => left.ClockDouble < right.ClockDouble;

        public static bool operator <=(McuTimestamp left, McuTimestamp right)
            => left.ClockDouble <= right.ClockDouble;

        public static bool operator >(McuTimestamp left, McuTimestamp right)
            => left.ClockDouble > right.ClockDouble;

        public static bool operator >=(McuTimestamp left, McuTimestamp right)
            => left.ClockDouble >= right.ClockDouble;
    }
}
