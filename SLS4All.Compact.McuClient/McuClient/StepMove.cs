// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.VisualBasic;
using SLS4All.Compact.McuClient.Pins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public enum StepMoveType
    {
        NotSet = 0,
        Pwm,
        Dwell,
        Move,
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly record struct StepMoveV1(uint Interval, short Count, short Add)
    {
        public const long MaxInterval = int.MaxValue - 1;

        public StepMoveType Type
        {
            get
            {
                if (Interval == uint.MaxValue)
                    return StepMoveType.Pwm;
                else if (Count == 0)
                    return StepMoveType.Dwell;
                else
                    return StepMoveType.Move;
            }
        }

        public long Duration
        {
            get
            {
                if (Interval == uint.MaxValue) // pwm
                    return 0;
                else if (Count == 0) // dwell
                    return Interval;
                else // step
                {
                    var count = Math.Abs(Count);
                    if (Add == 0)
                        return (long)Interval * count;
                    else
                        return (long)Add * count * (count - 1) / 2 + (long)Interval * count;
                }
            }
        }

        public ushort Power
        {
            get
            {
                Debug.Assert(Type == StepMoveType.Pwm);
                return (ushort)Count;
            }
        }

        public static StepMoveV1 Dwell(uint interval)
        {
            Debug.Assert(interval > 0 && interval <= MaxInterval);
            var res = new StepMoveV1(interval, 0, 0);
            Debug.Assert(res.Type == StepMoveType.Dwell);
            return res;
        }

        public static StepMoveV1 Pwm(ushort power)
        {
            var res = new StepMoveV1(uint.MaxValue, (short)power, 0);
            Debug.Assert(res.Type == StepMoveType.Pwm);
            return res;
        }

        public static StepMoveV1 Move(uint interval, short count, short add)
        {
            Debug.Assert(interval > 0 && interval <= MaxInterval);
            Debug.Assert(count != 0);
            var res = new StepMoveV1(interval, count, add);
            Debug.Assert(res.Type == StepMoveType.Move);
            return res;
        }

        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"T={Type}, I={Interval}, C={Count}, A={Add}");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly record struct StepMoveV2(uint Interval, short Count)
    {
        public const long MaxInterval = int.MaxValue - 1;

        public StepMoveType Type
        {
            get
            {
                if (Interval == uint.MaxValue)
                    return StepMoveType.Pwm;
                else if (Count == 0)
                    return StepMoveType.Dwell;
                else
                    return StepMoveType.Move;
            }
        }

        public ushort Power
        {
            get
            {
                Debug.Assert(Type == StepMoveType.Pwm);
                return (ushort)Count;
            }
        }

        public static StepMoveV2 Create(in StepMoveV1 source)
        {
            switch (source.Type)
            {
                case StepMoveType.Move:
                    Debug.Assert(source.Add == 0);
                    return Move(source.Interval, source.Count);
                case StepMoveType.Dwell:
                    return Dwell(source.Interval);
                case StepMoveType.Pwm:
                    return Pwm(source.Power);
                default:
                    Debug.Assert(false);
                    return default;
            }
        }

        public static StepMoveV2 Dwell(uint interval)
        {
            Debug.Assert(interval > 0 && interval <= MaxInterval);
            var res = new StepMoveV2(interval, 0);
            Debug.Assert(res.Type == StepMoveType.Dwell);
            return res;
        }

        public static StepMoveV2 Pwm(ushort power)
        {
            var res = new StepMoveV2(uint.MaxValue, (short)power);
            Debug.Assert(res.Type == StepMoveType.Pwm);
            return res;
        }

        public static StepMoveV2 Move(uint interval, short count)
        {
            Debug.Assert(interval > 0 && interval <= MaxInterval);
            Debug.Assert(count != 0);
            var res = new StepMoveV2(interval, count);
            Debug.Assert(res.Type == StepMoveType.Move);
            return res;
        }

        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"T={Type}, I={Interval}, C={Count}");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly record struct StepMoveV3(ushort Interval, short Count)
    {
        public const long MaxInterval = ushort.MaxValue - 1;

        public StepMoveType Type
        {
            get
            {
                if (Interval == ushort.MaxValue)
                    return StepMoveType.Pwm;
                else if (Count == 0)
                    return StepMoveType.Dwell;
                else
                    return StepMoveType.Move;
            }
        }

        public ushort Power
        {
            get
            {
                Debug.Assert(Type == StepMoveType.Pwm);
                return (ushort)Count;
            }
        }

        public static StepMoveV3 Create(in StepMoveV1 source)
        {
            switch (source.Type)
            {
                case StepMoveType.Move:
                    Debug.Assert(source.Add == 0);
                    return Move((int)source.Interval, source.Count);
                case StepMoveType.Dwell:
                    return Dwell((int)source.Interval);
                case StepMoveType.Pwm:
                    return Pwm(source.Power);
                default:
                    Debug.Assert(false);
                    return default;
            }
        }

        public static StepMoveV3 Dwell(int interval)
        {
            Debug.Assert(interval > 0 && interval < ushort.MaxValue);
            var res = new StepMoveV3((ushort)interval, 0);
            Debug.Assert(res.Type == StepMoveType.Dwell);
            return res;
        }

        public static StepMoveV3 Pwm(ushort power)
        {
            var res = new StepMoveV3(ushort.MaxValue, (short)power);
            Debug.Assert(res.Type == StepMoveType.Pwm);
            return res;
        }

        public static StepMoveV3 Move(int interval, short count)
        {
            Debug.Assert(interval > 0 && interval < ushort.MaxValue);
            Debug.Assert(count != 0);
            var res = new StepMoveV3((ushort)interval, count);
            Debug.Assert(res.Type == StepMoveType.Move);
            return res;
        }

        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"T={Type}, I={Interval}, C={Count}");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public readonly record struct StepMoveV4(uint Delay, ushort Power)
    {
        public const long MaxInterval = int.MaxValue - 1;

        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"D={Delay}, P={Power}");
    }
}
