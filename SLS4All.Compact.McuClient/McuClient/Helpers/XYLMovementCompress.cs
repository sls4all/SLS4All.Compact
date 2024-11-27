// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Temperature;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Data.Common;
using System.Diagnostics;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Helpers
{
    public sealed class XYLMovementCompress
    {
        private readonly record struct TodoEntry(int First, int Second);

        private readonly PrimitiveDeque<TodoEntry> _todo = new();
        private readonly PrimitiveList<double> _weights = new();
        private readonly PrimitiveList<double> _weightsSorted = new();
        private readonly PrimitiveList<XYLMovementPoint> _intermediate = new();

        private double CalcMaxWeight(double minEpsilon, int targetCount)
        {
            // NOTE: try to avoid sorting
            var pickIndex = _weights.Count - targetCount;
            if (pickIndex <= 0)
            {
                var span = _weights.Span;
                var minWeight = double.MaxValue;
                for (int i = 0; i < span.Length; i++)
                {
                    var weight = span[i];
                    if (weight < minWeight)
                        minWeight = weight;
                }
                return minWeight;
            }
            else if (pickIndex >= _weights.Count - 1)
            {
                var span = _weights.Span;
                var maxWeight = double.MinValue;
                for (int i = 0; i < span.Length; i++)
                {
                    var weight = span[i];
                    if (weight > maxWeight)
                        maxWeight = weight;
                }
                return maxWeight;
            }
            else
            {
                // NOTE: try to avoid sorting even here, use minEpilon count
                var span = _weights.Span;
                var minEpsilonCount = 0;
                for (int i = 0; i < span.Length; i++)
                {
                    var weight = span[i];
                    if (weight <= minEpsilon)
                        minEpsilonCount++;
                }

                if (pickIndex < minEpsilonCount)
                    return double.MinValue;
                else
                {
                    _weightsSorted.CopyFrom(_weights.Span);
                    _weightsSorted.Span.Sort();
                    var maxWeight = _weightsSorted[pickIndex];
                    return maxWeight;
                }
            }
        }

        public void CompressMoves(
            Span<XYLMovementPoint> source,
            PrimitiveList<XYLMovementPoint> final,
            int targetCount,
            double minEpsilon)
        {
            var originalCount = source.Length;
            if (originalCount > 2) // NOTE: always try to reduce, even if count is not above targetCount due to minEpsilon processing
            {
                var todo = _todo;
                todo.Clear();

                _weights.Count = originalCount;
                var weights = _weights.Span;

#if DEBUG
                weights.Fill(double.MinValue);
#endif
                weights[0] = double.MaxValue;
                weights[^1] = double.MaxValue;
                todo.PushBack() = new TodoEntry(0, source.Length - 1);

                while (todo.Count != 0)
                {
                    var pair = todo.PopFront();

                    Debug.Assert(pair.Second < originalCount);
                    Debug.Assert(pair.First < pair.Second);
                    Debug.Assert(weights[pair.First] != double.MinValue);
                    Debug.Assert(weights[pair.Second] != double.MinValue);

                    var maxDistance = double.MinValue;
                    var maxDistanceIndex = pair.Second;
                    var line = new XYLMovementPoint.Line(source[pair.First], source[pair.Second]);
                    for (int i = pair.First + 1; i < pair.Second; ++i)
                    {
                        ref var item = ref source[i];
                        Debug.Assert(!item.IsReset);

                        var distance = line.AbsDistance(item);
                        if (distance > maxDistance)
                        {
                            maxDistanceIndex = i;
                            maxDistance = distance;
                        }
                    }
                    if (maxDistance <= minEpsilon) // if the max distance is below epsilon, there is no need to subdivide more
                    {
                        weights[(pair.First + 1)..pair.Second].Fill(double.MinValue);
                    }
                    else
                    {
                        weights[maxDistanceIndex] = maxDistance;
                        if (maxDistanceIndex - pair.First > 1)
                            todo.PushBack() = new TodoEntry(pair.First, maxDistanceIndex);
                        if (pair.Second - maxDistanceIndex > 1)
                            todo.PushBack() = new TodoEntry(maxDistanceIndex, pair.Second);
                    }
                }
                var maxWeight = CalcMaxWeight(minEpsilon, targetCount);

                // prune points
                for (int i = 0; i < originalCount; i++)
                {
                    var weight = weights[i];
                    if ((weight >= maxWeight && // selected weight
                         weight > minEpsilon) || // automaticaly reduce if weight is very small even if not asked
                        (i == 0 || i + 1 == originalCount)) // always add first and last point
                        final.Add() = source[i];
                }
            }
            else
            {
                final.AddRange(source);
            }

            AddLast(source, final);
        }

        private static void AddLast(Span<XYLMovementPoint> source, PrimitiveList<XYLMovementPoint> final)
        {
            if (source.Length > 0)
            {
                ref var lastSource = ref source[^1];
                if (final.Count == 0)
                    final.Add() = lastSource;
                else
                {
                    ref var lastFinal = ref final[^1];
                    Debug.Assert(lastFinal.Time <= lastSource.Time);
                    if (lastFinal != lastSource) // add even if Value is the same, only if pair equals skip (to keep the queue alive)
                        final.Add() = lastSource;
                }
            }
        }

        public void CompressPwm(
            Span<XYLMovementPoint> source,
            PrimitiveList<XYLMovementPoint> final,
            int targetCount,
            double minTimeEpsilon,
            double minPowerEpsilon)
        {
            if (source.Length > 2) // NOTE: always try to reduce, even if count is not above targetCount due to minTimeEpsilon processing
            {
                // before everything, automaticaly remove states that are less than minimum epsilons, or on/off states are intersecting due to latency compensation
                var intermediateList = _intermediate;
                intermediateList.Clear();
                for (int i = 0; i < source.Length; i++)
                {
                    ref var item = ref source[i];
                    Debug.Assert(!item.IsReset);
                    if (intermediateList.Count != 0)
                    {
                        ref var prev = ref intermediateList[^1];
                        if (item.Value == 0) // should be off
                        {
                            if (prev.Value == 0) // was off, do nothing
                                continue;
                            else // was on
                            {
                                if (item.Time - prev.Time <= minTimeEpsilon) // was on for not enough time
                                {
                                    // remove previous "on"
                                    if (--intermediateList.Count == 0) // nothing left
                                    {
                                        // insert this off and go to next point
                                        intermediateList.Add() = item;
                                    }
                                    else
                                    {
                                        prev = ref intermediateList[^1];
                                        if (prev.Value == 0) // off now preceeds
                                        {
                                            // leave it there, skip current point, do nothing
                                        }
                                        else // on now preceeds
                                        {
                                            if (item.Time - prev.Time > minTimeEpsilon) // enough time between
                                            {
                                                // insert this off and go to next point
                                                intermediateList.Add() = item;
                                            }
                                            else // replace previous point with off (last resort)
                                                prev = new XYLMovementPoint(prev.Time, 0);
                                        }
                                    }
                                    continue;
                                }
                            }
                        }
                        else // should be on
                        {
                            if (prev.Value != 0) // was on
                            {
                                if (Math.Abs(item.Value - prev.Value) <= minPowerEpsilon) // on power changed too little, keep previous power
                                    continue;
                            }
                            else if (item.Time - prev.Time <= minTimeEpsilon) // was off for too little time, remove previous off
                            {
                                // remove previous "off"
                                if (--intermediateList.Count == 0) // nothing left
                                {
                                    // insert this on and go to next point
                                    intermediateList.Add() = item;
                                }
                                else
                                {
                                    prev = ref intermediateList[^1];
                                    Debug.Assert(prev.Value != 0);
                                    // on now preceeds
                                    if (item.Time - prev.Time > minTimeEpsilon) // enough time between
                                    {
                                        if (Math.Abs(item.Value - prev.Value) <= minPowerEpsilon) // on power changed too little, keep previous power
                                            continue;
                                        // insert this on and go to next point
                                        intermediateList.Add() = item;
                                    }
                                    else // replace previous point with this on (last resort)
                                        prev = new XYLMovementPoint(prev.Time, item.Value);
                                }
                                continue;
                            }
                        }
                    }
                    // add point
                    intermediateList.Add() = item;
                }
                if (intermediateList.Count == 0)
                {
                    ref var first = ref source[0];
                    ref var last = ref source[^1];
                    intermediateList.Add(first);
                    if ((first.Value != 0) != (last.Value != 0) ||
                        Math.Abs(last.Value - first.Value) > minPowerEpsilon)
                        intermediateList.Add(last);
                }

                var intermediate = intermediateList.Span;
                if (intermediate.Length > targetCount) // still too much points
                {
                    // calc duration for off periods
                    Debug.Assert(intermediate.Length > 0);
                    var weights = _weights;
                    weights.Clear();
                    for (int i = 1; i < intermediate.Length - 1; i++)
                    {
                        ref var a = ref intermediate[i - 1];
                        ref var b = ref intermediate[i];
                        ref var c = ref intermediate[i + 1];
                        if (a.Value != 0 && b.Value == 0 && c.Value != 0) // transition from on to off and back to on
                        {
                            var weight = c.Time - b.Time; // duration of OFF state
                            Debug.Assert(weight >= 0);
                            weights.Add() = weight;
                        }
                    }
                    _weights.Span.Sort();
                    var removed = (Math.Max(intermediate.Length - targetCount, 0) + 1) / 2;
                    Debug.Assert(removed > 0);

                    // prune points
                    final.Add() = intermediate[0];
                    if (removed < _weights.Count)
                    {
                        var maxWeight = _weights[removed - 1];
                        for (int i = 1; i < intermediate.Length - 1; i++)
                        {
                            ref var a = ref intermediate[i - 1];
                            ref var b = ref intermediate[i];
                            ref var c = ref intermediate[i + 1];
                            if (a.Value != 0 && b.Value == 0 && c.Value != 0) // transition from on to off and back to on
                            {
                                var weight = c.Time - b.Time; // duration of OFF state
                                Debug.Assert(weight >= 0);
                                if (weight <= maxWeight) // point of transition to off, that is less than maxWeight
                                {
                                    i++;
                                    ref var prev = ref final[^1];
                                    ref var next = ref c;
                                    Debug.Assert(prev.Value != 0);
                                    Debug.Assert(next.Value != 0);
                                    if (Math.Abs(next.Value - prev.Value) > minPowerEpsilon) // enough power difference, replace previous power
                                        prev = new XYLMovementPoint(prev.Time, next.Value);
                                    continue;
                                }
                            }
                            final.Add() = intermediate[i];
                        }
                    }
                }
                else
                {
                    final.AddRange(intermediate);
                }
            }
            else
            {
                final.AddRange(source);
            }

            AddLast(source, final);
        }
    }
}
