// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Threading;
using System;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace SLS4All.Compact.Slicing
{
    public readonly record struct SliceProcessResultSegment
    {
        private readonly int _index;
        private readonly int _countRaw;

        public int Index => _index;
        public int Count => _countRaw < 0 ? -_countRaw : _countRaw;
        public bool IsFill => _countRaw < 0;

        public SliceProcessResultSegment(bool isFill, int index, int count)
        {
            _index = index;
            _countRaw = isFill ? -count : count;
        }
    }

    public readonly record struct SliceEdgeType
    {
        private readonly int _rawValue;

        public static SliceEdgeType FirstOutline => new SliceEdgeType(1);
        public static SliceEdgeType Fill => new SliceEdgeType(0);

        public bool IsFill => _rawValue == 0;
        public int OutlineIndex => _rawValue - 1;

        private SliceEdgeType(int rawValue)
        {
            _rawValue = rawValue;
        }

        public static SliceEdgeType NthOutline(int index)
        {
            Debug.Assert(index >= 0);
            return new SliceEdgeType(index + 1);
        }
    }

    public struct SliceProcessorEdge
    {
        public SlicerEdge Edge;
        public SliceEdgeType Type;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SliceProcessorEdge(SlicerEdge edge, SliceEdgeType type)
        {
            Edge = edge;
            Type = type;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SliceProcessorEdge(Vector2 a, Vector2 b, Vector2 n, SliceEdgeType type)
        {
            Edge = new SlicerEdge(a, b, n);
            Type = type;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public SliceProcessorEdge ReverseDirection()
            => new SliceProcessorEdge(Edge.ReverseDirection(), Type);
    }

    public static class SliceProcessorEdgeExtensions
    {
        private abstract class SlicerEdgeGetter : IStrideSpanReferenceGetter<SliceProcessorEdge, SlicerEdge>
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public static ref SlicerEdge GetReference(ref SliceProcessorEdge source)
                => ref source.Edge;
        }

        public static StrideSpan<SlicerEdge> AsSlicerEdges(this Span<SliceProcessorEdge> span)
            => span.AsStrideSpan<SliceProcessorEdge, SlicerEdge, SlicerEdgeGetter>();
    }

    public struct SliceEstimateResult
    {
        public required int CompensationCount { get; set; }
        public required double[] OutlineLengths { get; set; }
        public required double FillLength { get; set; }
        public required double OffLength { get; set; }
    }

    /// <remarks>
    /// Class MUST be serializable!
    /// </remarks>
    public struct SliceProcessResult
    {
        public required int OutlineEdgeCount { get; set; }
        public required int FillEdgeCount { get; set; }
        public int TotalEdgeCount => OutlineEdgeCount + FillEdgeCount;
        public required double TotalLength { get; set; }
        public required double FillArea { get; set; }
        public required int[][] NthOutlineEdgeIndexes { get; set; }

        /// <summary>
        /// Returns edge "type". 0 for fill, 1, 2, 3, ... for first, second, third outline
        /// </summary>
        public SliceEdgeType GetEdgeType(int index)
        { 
            for (int i = 0; i < NthOutlineEdgeIndexes.Length; i++)
            {
                var indexes = NthOutlineEdgeIndexes[i];
                if (Array.BinarySearch(indexes, index) >= 0)
                    return SliceEdgeType.NthOutline(i);
            }
            return SliceEdgeType.Fill;
        }
    }

    public delegate double EstimateSliceEdgeTime(ref SliceProcessorEdge edge);

    [Flags]
    public enum SliceProcessorFlags
    {
        None = 0,
        /// <summary>
        /// Object will be treated in a way that allows very thin shapes to be printed. 
        /// This reduces dimensional and shape accuracy but allows it to be printed at all.
        /// This generally applies to shapes that are thinner than hotspot size in any given direction (note that hotspot may not be symmetrical).
        /// </summary>
        ThinObject = 1,
    }

    public interface ISliceProcessor
    {
        float HotspotOverlapFactor { get; set; }
        int OutlineCount { get; set; }
        int FillOutlineSkipCount { get; set; }
        bool IsFillEnabled { get; set; }
        int CompareWithFillPhase(int fillPhase, in Bounds3 left, in Bounds3 right);
        void PrepareSort(
            Span<SlicerEdge> sourceEdges,
            PrimitiveList<SlicerEdge> resultingEdges,
            float knownScale,
            CancellationTokenWrapper cancel);
        SliceEstimateResult Estimate(
            Span<SlicerEdge> preparedSortedEdges,
            Vector2 workingSize,
            int fillPhase,
            SliceProcessorFlags flags,
            CancellationTokenWrapper cancel);
        double Estimate(
            Span<SlicerEdge> preparedSortedEdges,
            EstimateSliceEdgeTime edgeFunc,
            Vector2 workingSize,
            int fillPhase,
            SliceProcessorFlags flags,
            CancellationTokenWrapper cancel);
        SliceProcessResult Process(
            Span<SlicerEdge> preparedSortedEdges,
            Vector2 workingSize,
            int fillPhase,
            SliceProcessorFlags flags,
            PrimitiveList<SlicerEdge> processedEdges,
            PrimitiveList<SlicerEdge>? processedSlicerEdges,
            CancellationTokenWrapper cancel);
    }
}