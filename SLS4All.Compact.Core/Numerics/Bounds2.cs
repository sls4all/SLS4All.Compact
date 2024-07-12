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
    public record struct Bounds2
    {
        public Vector2 Min;
        public Vector2 Max;
        public Vector2 Center => (Min + Max) * 0.5f;
        public Vector2 Size => Max - Min;
        public float SphereRadius => Vector2.Distance(Center, Min);

        public Bounds2(Vector2 min, Vector2 max)
        {
            Min = min;
            Max = max;
        }

        public Bounds2 IncreaseRadius(float size)
        {
            var step = new Vector2(size, size);
            return new Bounds2(Min - step, Max + step);
        }

        public static Bounds2 FromMinAndSize(Vector2 min, Vector2 size)
            => new Bounds2(min, min + size);

        public static Bounds2 operator +(Bounds2 bounds, Vector2 vector)
            => new Bounds2(bounds.Min + vector, bounds.Max + vector);

        public static Bounds2 operator -(Bounds2 bounds, Vector2 vector)
            => new Bounds2(bounds.Min - vector, bounds.Max - vector);

        public Bounds2 Flip()
            => new Bounds2(new Vector2(Min.Y, Min.X), new Vector2(Max.Y, Max.X));
    }
}
