// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static System.Formats.Asn1.AsnWriter;

namespace SLS4All.Compact.McuClient.Helpers
{
    [StructLayout(LayoutKind.Sequential)]
    public readonly record struct XYLMovementPoint(double TimeRaw, double Value)
    {
        public readonly struct Line
        {
            private readonly XYLMovementPoint _a;
            private readonly XYLMovementPoint _b;
            private readonly double _timeDiff;
            private readonly double _slope;

            public Line(in XYLMovementPoint a, in XYLMovementPoint b)
            {
                Debug.Assert(a.Time <= b.Time);
                _a = a;
                _b = b;
                _timeDiff = b.Time - a.Time;
                _slope = (b.Value - a.Value) / _timeDiff;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double AbsDistance(in XYLMovementPoint to)
            {
                Debug.Assert(_a.Time <= to.Time);
                Debug.Assert(_b.Time >= to.Time);
                if (_timeDiff == 0)
                    return Math.Abs(to.Value - _a.Value);
                else
                {
                    var value = _a.Value + (to.Time - _a.Time) * _slope;
                    return Math.Abs(to.Value - value);
                }
            }
        }

        public readonly struct LineXY
        {
            private readonly XYLMovementPoint _ax;
            private readonly XYLMovementPoint _ay;
            private readonly XYLMovementPoint _bx;
            private readonly XYLMovementPoint _by;
            private readonly double _len;
            private readonly double _nx;
            private readonly double _ny;

            public LineXY(
                in XYLMovementPoint ax, in XYLMovementPoint ay, 
                in XYLMovementPoint bx, in XYLMovementPoint by)
            {
                Debug.Assert(ax.Time <= bx.Time);
                Debug.Assert(ax.Time == ay.Time);
                Debug.Assert(bx.Time == by.Time);
                _ax = ax;
                _ay = ay;
                _bx = bx;
                _by = by;
                var dx = bx.Value - ax.Value;
                var dy = by.Value - ay.Value;
                _len = Math.Sqrt(dx * dx + dy * dy);
                _nx = _len > 0 ? dx / _len : 0;
                _ny = _len > 0 ? dy / _len : 0;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double DistanceSq(in XYLMovementPoint toX, in XYLMovementPoint toY)
            {
                Debug.Assert(_ax.Time <= toX.Time);
                Debug.Assert(_bx.Time >= toX.Time);
                Debug.Assert(toX.Time == toY.Time);
                double dx, dy;
                if (_len == 0)
                {
                    dx = toX.Value - _ax.Value;
                    dy = toY.Value - _ay.Value;
                }
                else
                {
                    var d = (toX.Value - _ax.Value) * _nx + (toY.Value - _ay.Value) * _ny;
                    if (d <= 0)
                    {
                        dx = toX.Value - _ax.Value;
                        dy = toY.Value - _ay.Value;
                    }
                    else if (d >= _len)
                    {
                        dx = toX.Value - _bx.Value;
                        dy = toY.Value - _by.Value;
                    }
                    else
                    {
                        dx = toX.Value - (_ax.Value + _nx * d);
                        dy = toY.Value - (_ay.Value + _ny * d);
                    }
                }
                return dx * dx + dy * dy;
            }
        }

        public readonly struct LineXYT
        {
            private readonly XYLMovementPoint _ax;
            private readonly XYLMovementPoint _ay;
            private readonly XYLMovementPoint _bx;
            private readonly XYLMovementPoint _by;
            private readonly double _timeDiff;
            private readonly double _dx;
            private readonly double _dy;

            public LineXYT(
                in XYLMovementPoint ax, in XYLMovementPoint ay,
                in XYLMovementPoint bx, in XYLMovementPoint by)
            {
                Debug.Assert(ax.Time <= bx.Time);
                Debug.Assert(ax.Time == ay.Time);
                Debug.Assert(bx.Time == by.Time);
                _ax = ax;
                _ay = ay;
                _bx = bx;
                _by = by;
                _timeDiff = bx.Time - ax.Time;
                _dx = (bx.Value - ax.Value) / _timeDiff;
                _dy = (by.Value - ay.Value) / _timeDiff;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public double DistanceSq(in XYLMovementPoint toX, in XYLMovementPoint toY)
            {
                Debug.Assert(_ax.Time <= toX.Time);
                Debug.Assert(_bx.Time >= toX.Time);
                Debug.Assert(toX.Time == toY.Time);
                double dx, dy;
                if (_timeDiff == 0)
                {
                    dx = toX.Value - _ax.Value;
                    dy = toY.Value - _ay.Value;
                }
                else
                {
                    dx = toX.Value - (_ax.Value + (toX.Time - _ax.Time) * _dx);
                    dy = toY.Value - (_ay.Value + (toY.Time - _ay.Time) * _dy);
                }
                return dx * dx + dy * dy;
            }
        }

        public double Time => Math.Abs(TimeRaw);
        public bool IsReset => TimeRaw < 0;
        public XYLMovementPoint CloneNoReset => new XYLMovementPoint(Time, Value);

        public override string ToString()
            => string.Create(CultureInfo.InvariantCulture, $"TimeRaw={TimeRaw}, Value={Value}");
    }
}
