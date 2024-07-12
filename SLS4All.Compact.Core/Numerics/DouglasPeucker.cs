// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Network;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Numerics
{
    public enum DouglasPeuckerPointType
    {
        NotSet = 0,
        Neccessary,
        Ignored,
    }

    public struct DouglasPeuckerPoint<T>
    {
        public readonly struct Line
        {
            internal readonly DouglasPeuckerPoint<T> _a;
            internal readonly DouglasPeuckerPoint<T> _b;
            internal readonly float _length;
            internal readonly Vector2 _dir;

            public Line(in DouglasPeuckerPoint<T> a, in DouglasPeuckerPoint<T> b)
            {
                _a = a;
                _b = b;
                _dir = b.Position - a.Position;
                _length = _dir.Length();
                if (_length > 0)
                    _dir /= _length;
            }
        }

        public T Source;
        public Vector2 Position;
        public DouglasPeuckerPointType Type;

        public DouglasPeuckerPoint(
            T source,
            Vector2 position,
            DouglasPeuckerPointType type)
        {
            Source = source;
            Position = position;
            Type = type;
        }

        public float DistanceSqTo(in Line line)
        {
            if (line._length == 0)
                return Vector2.DistanceSquared(Position, line._a.Position);
            else
            {
                var d = Vector2.Dot(Position - line._a.Position, line._dir);
                if (d <= 0)
                    return Vector2.DistanceSquared(Position, line._a.Position);
                else if (d >= line._length)
                    return Vector2.DistanceSquared(Position, line._b.Position);
                else
                {
                    var pos = line._a.Position + line._dir * d;
                    return Vector2.DistanceSquared(Position, pos);
                }
            }
        }
    }

    public delegate DouglasPeuckerPoint<T> DouglasPeuckerPointTransform<T>(T source);

    public sealed class DouglasPeucker<T>
    {
        private readonly record struct PointRange(int Left, int Right);

        private readonly PrimitiveDeque<PointRange> _stack;
        private readonly PrimitiveList<DouglasPeuckerPoint<T>> _results;

        public PrimitiveList<DouglasPeuckerPoint<T>> Items => _results;

        public DouglasPeucker()
        {
            _stack = new();
            _results = new();
        }

        public int GetNeccessaryCount()
        { 
            var neccessaryCount = 0;
            var span = _results.Span;
            for (int i = 0; i < span.Length; i++)
            {
                if (span[i].Type == DouglasPeuckerPointType.Neccessary)
                    neccessaryCount++;
            }
            return neccessaryCount;
        }

        public void Simplify(Span<T> source, float bias, DouglasPeuckerPointTransform<T> transform, Action<PrimitiveList<DouglasPeuckerPoint<T>>>? finalize = null)
        {
            _results.Clear();
            for (int i = 0; i < source.Length; i++)
            {
                _results.Add(transform(source[i]));
            }
            if (finalize != null && _results.Count > 2)
                finalize(_results);

            Simplify(bias);
        }

        public T[] NecessarySourcesToArray()
        {
            var count = GetNeccessaryCount();
            var res = count == 0 ? Array.Empty<T>() : new T[count];
            var span = _results.Span;
            for (int i = 0, q = 0; i < span.Length; i++)
            {
                ref var item = ref span[i];
                if (item.Type == DouglasPeuckerPointType.Neccessary)
                    res[q++] = item.Source;
            }
            return res;
        }

        public void Simplify(float bias)
        {
            var points = _results.Span;
            if (points.Length == 0)
                return;

            if (points.Length > 2)
            {
                _stack.Clear();

                int first;
                for (first = 0; first < points.Length; first++)
                {
                    if (points[first].Type != DouglasPeuckerPointType.Ignored)
                        break;
                }
                if (first == points.Length)
                    return;

                int last;
                for (last = points.Length - 1; last > first; last--)
                {
                    if (points[last].Type != DouglasPeuckerPointType.Ignored)
                        break;
                }

                points[first].Type = DouglasPeuckerPointType.Neccessary;
                points[last].Type = DouglasPeuckerPointType.Neccessary;

                if (first == last)
                    return;

                var left = first;
                var right = first + 1;
                do
                {
                    if (points[right].Type == DouglasPeuckerPointType.Neccessary)
                    {
                        Debug.Assert(left <= right);
                        if (left + 1 != right)
                            _stack.PushFront(new PointRange(left, right));
                        left = right;
                    }
                    ++right;
                }
                while (right <= last);

                var biasSq = bias * bias;
                while (_stack.Count != 0)
                {
                    var pair = _stack.PopFront();
                    Debug.Assert(points[pair.Left].Type == DouglasPeuckerPointType.Neccessary);
                    Debug.Assert(points[pair.Right].Type == DouglasPeuckerPointType.Neccessary);
                    Debug.Assert(pair.Right < points.Length);
                    Debug.Assert(pair.Left < pair.Right);
                    var maxDistanceSq = float.MinValue;
                    var maxIndex = pair.Right;
                    var line = new DouglasPeuckerPoint<T>.Line(points[pair.Left], points[pair.Right]);
                    for (int i = pair.Left + 1; i < pair.Right; ++i)
                    {
                        ref var point = ref points[i];
                        Debug.Assert(point.Type != DouglasPeuckerPointType.Neccessary);
                        if (point.Type == DouglasPeuckerPointType.Ignored)
                            continue;
                        var distanceSq = point.DistanceSqTo(line);
                        if (distanceSq > biasSq && distanceSq > maxDistanceSq)
                        {
                            maxIndex = i;
                            maxDistanceSq = distanceSq;
                        }
                    }
                    if (maxDistanceSq > biasSq)
                    {
                        points[maxIndex].Type = DouglasPeuckerPointType.Neccessary;
                        if (maxIndex - pair.Left > 1)
                            _stack.PushFront(new PointRange(pair.Left, maxIndex));
                        if (pair.Right - maxIndex > 1)
                            _stack.PushFront(new PointRange(maxIndex, pair.Right));
                    }
                }
            }
        }
    }
}
