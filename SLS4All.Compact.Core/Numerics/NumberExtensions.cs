// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Slicing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.Numerics
{
    public static class NumberExtensions
    {
        public static decimal? NullIfZeroOrLess(decimal? value)
            => value > 0 ? value : null;

        public static double? NullIfZeroOrLess(double? value)
            => value > 0 ? value : null;

        public static float? NullIfZeroOrLess(float? value)
            => value > 0 ? value : null;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Square(float x)
            => x * x;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double Square(double x)
            => x * x;

        public static float ClosestOfXYZ(this Vector3 vec, float center)
        {
            var dist = new Vector3(center) - vec;
            var closest = MathF.MinMagnitude(dist.X, MathF.MinMagnitude(dist.Y, dist.Z));
            return center - closest;
        }

        public static float MaxXYZ(this Vector3 vec)
            => Math.Max(Math.Max(vec.X, vec.Y), vec.Z);

        public static float SignedRadianDifference(float a, float b)
        {
            var diff = MathF.PI - MathF.Abs(MathF.Abs(a - b) - MathF.PI);
            var target1 = a + diff;
            var error1 = MathF.Min(MathF.Abs(target1 - b),
                MathF.Min(MathF.Abs(target1 - b - MathF.PI * 2),
                MathF.Abs(target1 - b + MathF.PI * 2)));
            var target2 = a - diff;
            var error2 = MathF.Min(MathF.Abs(target2 - b),
                MathF.Min(MathF.Abs(target2 - b - MathF.PI * 2),
                MathF.Abs(target2 - b + MathF.PI * 2)));
            if (error1 <= error2)
                return diff;
            else
                return -diff;
        }

        private static decimal RoundToDecimalInternal(decimal value, int decimals, decimal multipleOf = 1, bool zeroes = false)
        {
            if (multipleOf != 1)
                value = Math.Round(value / multipleOf) * multipleOf;
            if (zeroes)
            {
                var zeroesValue = decimals switch
                {
                    0 => 0M,
                    1 => 0.0M,
                    2 => 0.00M,
                    3 => 0.000M,
                    4 => 0.0000M,
                    5 => 0.00000M,
                    6 => 0.000000M,
                    _ => throw new ArgumentOutOfRangeException(nameof(decimals)),
                };
                return Math.Round(value + zeroesValue, decimals);
            }
            else
            {
                for (int i = decimals; i > 0; i--)
                {
                    var res1 = Math.Round(value, i);
                    var res2 = Math.Round(value, i - 1);
                    if (res1 != res2)
                        return res1;
                }
                return Math.Round(value);
            }
        }

        public static decimal RoundToDecimal(this double value, int decimals, decimal multipleOf = 1, bool zeroes = false)
            => RoundToDecimalInternal((decimal)value, decimals, multipleOf: multipleOf, zeroes: zeroes);

        public static decimal RoundToDecimal(this float value, int decimals, decimal multipleOf = 1, bool zeroes = false)
            => RoundToDecimalInternal((decimal)value, decimals, multipleOf: multipleOf, zeroes: zeroes);

        public static decimal RoundToDecimal(this decimal value, int decimals, decimal multipleOf = 1, bool zeroes = false)
            => RoundToDecimalInternal(value, decimals, multipleOf: multipleOf, zeroes: zeroes);

        /// <summary>
        /// https://forum.unity.com/threads/line-intersection.17384/#post-4442284
        /// </summary>
        public static bool HasIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2)
        {
            var a = a2 - a1;
            var b = b1 - b2;
            var c = a1 - b1;

            var alphaNumerator = b.Y * c.X - b.X * c.Y;
            var betaNumerator = a.X * c.Y - a.Y * c.X;
            var denominator = a.Y * b.X - a.X * b.Y;

            if (denominator == 0)
                return false;
            else if (denominator > 0)
            {
                if (alphaNumerator < 0 || alphaNumerator > denominator || betaNumerator < 0 || betaNumerator > denominator)
                    return false;
            }
            else if (alphaNumerator > 0 || alphaNumerator < denominator || betaNumerator > 0 || betaNumerator < denominator)
                return false;
            return true;
        }

        /// <summary>
        /// https://blog.dakwamine.fr/?p=1943 (modified)
        /// </summary>
        public static bool GetIntersection(Vector2 a1, Vector2 a2, Vector2 b1, Vector2 b2, out Vector2 result)
        {
            var ad = a2 - a1;
            var bd = b2 - b1;
            var tmp = bd.X * ad.Y - bd.Y * ad.X;
            if (tmp != 0)
            {
                var ar = a1 - b1;
                var fb = (ar.X * ad.Y - ar.Y * ad.X) / tmp;
                if (fb >= 0 && fb <= 1)
                {
                    var i = Vector2.Lerp(b1, b2, fb);
                    var fa2 = Vector2.Dot(i - a1, ad);
                    if (fa2 >= 0)
                    {
                        var adSq = ad.LengthSquared();
                        if (fa2 <= adSq)
                        {
                            result = i;
                            return true;
                        }
                    }
                }
            }
            result = default;
            return false;
        }
    }
}
