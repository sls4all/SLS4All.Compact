// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Numerics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Slicing
{
    /// <summary>
    /// Represents a sliced 3D edge with normal (not normalized) made from 3D mesh face
    /// </summary>
    public record struct SlicerEdge
    {
        public Vector2 a;
        public Vector2 b;
        public Vector2 n;

        /// <summary>
        /// Creates compensation point/edge
        /// </summary>
        public SlicerEdge(Vector2 ab, Vector2 n)
        {
            this.a = ab;
            this.b = ab;
            this.n = n;
        }

        public SlicerEdge(Vector2 a, Vector2 b, Vector2 n)
        {
            this.a = a;
            this.b = b;
            this.n = n;
        }

        public readonly Vector2 Min => new Vector2(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y));
        public readonly Vector2 Max => new Vector2(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y));
        public readonly bool IsNotValidEdgeIsPoint => a == b;
        public readonly Vector2 Direction => b - a;
        public readonly Vector2 PossiblyReversedNormalizedDirection => new Vector2(n.Y, -n.X);
        public readonly float SignedLengthUsingNormal => Vector2.Dot(Direction, PossiblyReversedNormalizedDirection);
        public readonly float LengthUsingNormal => MathF.Abs(SignedLengthUsingNormal);
        public readonly float LengthUsingNormalOrVector => n == Vector2.Zero ? (a == b ? 0 : Vector2.Distance(a, b)) : MathF.Abs(SignedLengthUsingNormal);
        public readonly float LengthUsingVector => Vector2.Distance(a, b);
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly SlicerEdge ReverseDirection()
            => new SlicerEdge(b, a, n);
        public readonly float GetSignedDoubledArea()
            => a.X * b.Y - a.Y * b.X;
        public readonly float GetSignedArea()
            => GetSignedDoubledArea() * 0.5f;

        public readonly float GetDistanceSq(Vector2 origin)
        {
            Debug.Assert(n != Vector2.Zero);
            var along = Vector2.Dot(origin - a, PossiblyReversedNormalizedDirection);
            if (along < 0)
                return Vector2.DistanceSquared(origin, a);
            else if (along > 1)
                return Vector2.DistanceSquared(origin, b);
            else
            {
                var dist = Vector2.Dot(origin - a, n);
                return dist * dist;
            }
        }
    }

    public static class SlicerEdgeExtensions
    {
        public static (float AB, float NAB) GetSignedArea(this StrideSpan<SlicerEdge> edges, bool wantsNAB)
        {
            var ab = 0.0;
            var nab = 0.0;
            for (int i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                var value = edge.GetSignedDoubledArea();
                ab += value;
                if (wantsNAB)
                {
                    if (Vector2.Dot(edge.Direction, edge.PossiblyReversedNormalizedDirection) >= 0)
                        nab += value;
                    else
                        nab -= value;
                }
            }
            return ((float)(ab * 0.5), (float)(nab * 0.5));
        }

        public static Bounds2 GetBounds(this StrideSpan<SlicerEdge> edges)
        {
            if (edges.Length == 0)
                return new Bounds2(Vector2.Zero, Vector2.Zero);
            ref var first = ref edges[0];
            var minX = MathF.Min(first.a.X, first.b.X);
            var minY = MathF.Min(first.a.Y, first.b.Y);
            var maxX = MathF.Max(first.a.X, first.b.X);
            var maxY = MathF.Max(first.a.Y, first.b.Y);
            for (int i = 1; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                if (edge.a.X < minX)
                    minX = edge.a.X;
                else if (edge.a.X > maxX)
                    maxX = edge.a.X;
                if (edge.b.X < minX)
                    minX = edge.b.X;
                else if (edge.b.X > maxX)
                    maxX = edge.b.X;
                if (edge.a.Y < minY)
                    minY = edge.a.Y;
                else if (edge.a.Y > maxY)
                    maxY = edge.a.Y;
                if (edge.b.Y < minY)
                    minY = edge.b.Y;
                else if (edge.b.Y > maxY)
                    maxY = edge.b.Y;
            }
#if DEBUG
            for (int i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                Debug.Assert(
                    edge.a.X >= minX &&
                    edge.a.Y >= minY &&
                    edge.a.X <= maxX &&
                    edge.a.Y <= maxY);
            }
#endif
            return new Bounds2(new Vector2(minX, minY), new Vector2(maxX, maxY));
        }

        public static void ReverseEdges(this Span<SlicerEdge> edges, bool reversePoints, bool invertNormal)
        {
            if (!reversePoints && !invertNormal)
                return;
            for (int i = 0; i < edges.Length; i++)
            {
                ref var item = ref edges[i];
                if (reversePoints)
                    (item.a, item.b) = (item.b, item.a);
                if (invertNormal)
                    item.n = -item.n;
            }
            if (reversePoints)
                edges.Reverse();
        }

        public static bool ContainsPoint(this StrideSpan<SlicerEdge> edges, bool isHole, Vector2 pos)
        {
            var bounds = GetBounds(edges);
            return ContainsPoint(edges, isHole, bounds.Min, bounds.Max, pos);
        }

        public static bool ContainsPoint(this StrideSpan<SlicerEdge> edges, bool isHole, Vector2 min, Vector2 max, Vector2 pos)
        {
            if (pos.X < min.X || pos.X > max.X ||
                pos.Y < min.Y || pos.Y > max.Y)
                return false;
            var pos0 = new Vector2(min.X - (max.X - min.X) * 0.1f, pos.Y);
            var left = float.MinValue;
            var leftDir = 0.0;

            for (int i = 0; i < edges.Length; i++)
            {
                ref var edge = ref edges[i];
                if (NumberExtensions.HasIntersection(pos0, pos, edge.a, edge.b) &&
                    NumberExtensions.GetIntersection(pos0, pos, edge.a, edge.b, out var intersection))
                {
                    if (intersection.X > left && edge.n.X != 0)
                    {
                        left = intersection.X;
                        leftDir = edge.n.X;
                    }
                }
            }
            var contains = !isHole ? leftDir < 0 : leftDir > 0;
            return contains;
        }

        //public static bool ContainsPoint(this StrideSpan<SlicerEdge> edges, bool isHole, Vector2 min, Vector2 max, Vector2 pos)
        //{
        //    if (pos.X < min.X || pos.X > max.X ||
        //        pos.Y < min.Y || pos.Y > max.Y)
        //        return false;
        //    var sizePart = (max - min) * 0.1f;
        //    var posX0 = new Vector2(min.X - sizePart.X, pos.Y);
        //    var posX1 = new Vector2(max.X + sizePart.X, pos.Y);
        //    var posY0 = new Vector2(pos.X, min.Y - sizePart.Y);
        //    var posY1 = new Vector2(pos.X, max.Y + sizePart.Y);
        //    (float pos, float dir)
        //        left = (float.MinValue, 0.0f),
        //        right = (float.MaxValue, 0.0f),
        //        top = (float.MinValue, 0.0f),
        //        bottom = (float.MaxValue, 0.0f);

        //    for (int i = 0; i < edges.Length; i++)
        //    {
        //        ref var edge = ref edges[i];
        //        if (edge.n.X != 0)
        //        {
        //            if (NumberExtensions.GetIntersection(posX0, posX1, edge.a, edge.b, out var intersection))
        //            {
        //                if (intersection.X < pos.X && intersection.X > left.pos)
        //                    left = (intersection.X, edge.n.X);
        //                if (intersection.X > pos.X && intersection.X < right.pos)
        //                    right = (intersection.X, edge.n.X);
        //            }
        //        }
        //        if (edge.n.Y != 0)
        //        {
        //            if (NumberExtensions.GetIntersection(posY0, posY1, edge.a, edge.b, out var intersection))
        //            {
        //                if (intersection.Y < pos.Y && intersection.Y > top.pos)
        //                    top = (intersection.Y, edge.n.Y);
        //                if (intersection.Y > pos.Y && intersection.Y < bottom.pos)
        //                    bottom = (intersection.Y, edge.n.Y);
        //            }
        //        }
        //    }
        //    if (isHole)
        //        return left.dir > 0 && right.dir < 0 && top.dir > 0 && bottom.dir < 0;
        //    else
        //        return left.dir < 0 && right.dir > 0 && top.dir < 0 && bottom.dir > 0;
        //}
    }
}
