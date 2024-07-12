// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Numerics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization.Formatters;
using System.Text;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.Slicing
{
    public static class SlicerExtensions
    {
        /// <summary>
        /// Returns if there is a intersection between specified <paramref name="edges"/> and line from <paramref name="a"/> to <paramref name="b"/>.
        /// If the point <paramref name="a"/> lays on the edges, that does not count as a intersection.
        /// </summary>
        public static bool TryGetIntersection(this Span<SlicerEdge> edges, Vector2 a, Vector2 b, out Vector2 nearestIntersection)
        {
            const float onEdgeTolerance = 0.001f;
            var nearestIntersectionDistanceSq = float.MaxValue;
            nearestIntersection = default;

            for (int i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                if (edge.a == a || edge.b == a)
                    continue;
                if (NumberExtensions.HasIntersection(edge.a, edge.b, a, b) &&
                    NumberExtensions.GetIntersection(edge.a, edge.b, a, b, out var intersection))
                {
                    var distanceSq = Vector2.DistanceSquared(intersection, a);
                    if (distanceSq <= onEdgeTolerance * onEdgeTolerance)
                        continue;
                    if (distanceSq < nearestIntersectionDistanceSq)
                    {
                        nearestIntersectionDistanceSq = distanceSq;
                        nearestIntersection = intersection;
                    }
                }
            }
            return nearestIntersectionDistanceSq != float.MaxValue;
        }
    }
}
