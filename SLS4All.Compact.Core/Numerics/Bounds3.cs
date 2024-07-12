// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Numerics
{
    public record struct Bounds3
    {
        public Vector3 Min;
        public Vector3 Max;
        public Vector3 Center => (Min + Max) * 0.5f;
        public Vector3 Size => Max - Min;
        public float SphereRadius => Vector3.Distance(Center, Min);

        public Bounds3(Vector3 min, Vector3 max)
        {
            Min = min;
            Max = max;
        }

        public Bounds3 IncreaseRadius(float size)
        {
            var step = new Vector3(size, size, size);
            return new Bounds3(Min - step, Max + step);
        }

        public static Bounds3 FromMinAndSize(Vector3 min, Vector3 size)
            => new Bounds3(min, min + size);

        public static Bounds3 operator +(Bounds3 bounds, Vector3 vector)
            => new Bounds3(bounds.Min + vector, bounds.Max + vector);

        public static Bounds3 operator -(Bounds3 bounds, Vector3 vector)
            => new Bounds3(bounds.Min - vector, bounds.Max - vector);
    }
}
