// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Validation;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Numerics
{
    public class GridOptimizer
    {
        private sealed class SolutionComparer : IEqualityComparer<ArraySegment<double>>
        {
            public readonly static SolutionComparer Instance = new SolutionComparer();
            public bool Equals(ArraySegment<double> x, ArraySegment<double> y)
            {
                if (x.Count != y.Count) 
                    return false;
                return x.AsSpan(0, x.Count).SequenceEqual(y.AsSpan(0, y.Count));
            }

            public int GetHashCode([DisallowNull] ArraySegment<double> obj)
            {
                var hash = new HashCode();
                hash.AddBytes(MemoryMarshal.AsBytes(obj.AsSpan(0, obj.Count)));
                return hash.ToHashCode();
            }
        }

        private readonly int _parameterCount;
        private readonly Dictionary<ArraySegment<double>, double> _cache;
        private double[] _values = Array.Empty<double>();

        public int ParameterCount => _parameterCount;
        public bool[] LowerBoundsFixed { get; set; }
        public bool[] UpperBoundsFixed { get; set; }
        public double[] LowerBounds { get; set; }
        public double[] UpperBounds { get; set; }
        public double[] Solution { get; set; }
        public int[] Cells { get; set; }
        public Func<double[], double> Function { get; set; }
        public Func<double[], CancellationToken, double>? FunctionWithCancellation { get; set; }
        public int MaxIterations { get; set; } = 32;
        public int Iterations { get; private set; }
        public double Value { get; set; }
        public bool Caching { get; set; }
        public bool IsSolutionOnBoundary
        {
            get
            {
                for (int i = 0; i < _parameterCount; i++)
                {
                    var s = Solution[i];
                    if (s == UpperBounds[i] || s == LowerBounds[i])
                        return true;
                }
                return false;
            }
        }

        public GridOptimizer(
            int parameterCount,
            Func<double[], double>? objectiveFunc = null)
        {
            _parameterCount = parameterCount;
            LowerBoundsFixed = new bool[parameterCount];
            UpperBoundsFixed = new bool[parameterCount];
            Array.Fill(LowerBoundsFixed, true);
            Array.Fill(UpperBoundsFixed, true);
            LowerBounds = new double[parameterCount];
            UpperBounds = new double[parameterCount];
            Solution = new double[parameterCount];
            Cells = new int[parameterCount];
            _cache = new(SolutionComparer.Instance);
            Array.Fill(Cells, 3);
            Function = objectiveFunc ?? (values => 0);
        }

        private void CoordToSolution(Span<double> solution, Span<int> coord, Span<double> upperBounds, Span<double> lowerBounds)
        {
            for (int axis = 0; axis < _parameterCount; axis++)
            {
                var cell = coord[axis];
                var cells = Cells[axis];
                var upper = upperBounds[axis];
                var lower = lowerBounds[axis];
                var range = upper - lower;
                var pos = cell == 0 ? lower : cell == cells ? upper : lower + range * cell / cells;
                solution[axis] = pos;
            }
        }

        public bool Minimize(CancellationToken cancel = default)
        {
            var cellsTotal = 1;
            foreach (var cell in Cells)
                cellsTotal *= (cell + 1);
            var lowerBounds = LowerBounds.ToArray().AsSpan();
            var upperBounds = UpperBounds.ToArray().AsSpan();
            var lowerBounds2 = LowerBounds.ToArray().AsSpan();
            var upperBounds2 = UpperBounds.ToArray().AsSpan();
            var solution = Solution;
            var cache = Caching ? _cache : null;
            ReturnCache(cache);
            double[] values;
            if (_values.Length != cellsTotal)
                _values = values = new double[cellsTotal];
            else
                values = _values;
            Span<int> coords = stackalloc int[_parameterCount * cellsTotal];
            Span<int> coord = stackalloc int[_parameterCount];
            Span<int> coordLower = stackalloc int[_parameterCount];
            Span<int> coordUpper = stackalloc int[_parameterCount];
            var iterations = 0;
            var totalBest = double.MaxValue;
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                var valueIndex = 0;
                coord.Fill(0);
                while (true)
                {
                    CoordToSolution(solution, coord, upperBounds, lowerBounds);
                    double value;
                    if (cache == null || !cache.TryGetValue(solution, out value))
                    {
                        if (FunctionWithCancellation != null)
                            value = FunctionWithCancellation(solution, cancel);
                        else
                            value = Function(solution);
                        if (cache != null)
                        {
                            var clone = new ArraySegment<double>(ArrayPool<double>.Shared.Rent(_parameterCount), 0, _parameterCount);
                            solution.CopyTo(clone.Array!, 0);
                            cache.Add(clone, value);
                        }
                    }
                    values[valueIndex] = value;
                    coord.CopyTo(coords.Slice(valueIndex * _parameterCount));
                    valueIndex++;

                    var doneAllAxis = false;
                    for (int axis = 0; ;)
                    {
                        ref var c = ref coord[axis];
                        if (++c <= Cells[axis])
                            break;
                        c = 0;
                        if (++axis == _parameterCount)
                        {
                            doneAllAxis = true;
                            break;
                        }
                    }
                    if (doneAllAxis)
                        break;
                }
                // find minumum value
                valueIndex = 0;
                var best = (value: double.MaxValue, cellMin: 0, cellMax: 0);
                for (int cell = 0; cell < cellsTotal; cell++)
                {
                    var value = values[cell];
                    if (value < best.value)
                        best = (value, cell, cell);
                    else if (value == best.value)
                        best.cellMax = cell;
                }
                coords.Slice(best.cellMin * _parameterCount, _parameterCount).CopyTo(coordLower);
                coords.Slice(best.cellMax * _parameterCount, _parameterCount).CopyTo(coordUpper);
                if (++iterations >= MaxIterations)
                {
                    CoordToSolution(solution, coordLower, upperBounds, lowerBounds);
                    break;
                }
                totalBest = best.value;
                for (int axis = 0; axis < _parameterCount; axis++)
                {
                    ref int c = ref coordLower[axis];
                    if (!LowerBoundsFixed[axis])
                    {
                        if (c == 0)
                            c -= Cells[axis];
                        else
                            c--;
                    }
                    else
                    {
                        if (c > 0)
                            c--;
                    }
                }
                for (int axis = 0; axis < _parameterCount; axis++)
                {
                    ref int c = ref coordUpper[axis];
                    var cs = Cells[axis];
                    if (!UpperBoundsFixed[axis])
                    {
                        if (c == cs)
                            c += cs;
                        else
                            c++;
                    }
                    else
                    {
                        if (c < cs)
                            c++;
                    }
                }
                CoordToSolution(lowerBounds2, coordLower, upperBounds, lowerBounds);
                CoordToSolution(upperBounds2, coordUpper, upperBounds, lowerBounds);
                lowerBounds2.CopyTo(lowerBounds);
                upperBounds2.CopyTo(upperBounds);
            }
            ReturnCache(cache);
            Iterations = iterations;
            Value = totalBest;
            return true;
        }

        private static void ReturnCache(Dictionary<ArraySegment<double>, double>? cache)
        {
            if (cache != null)
            {
                foreach (var array in cache.Keys)
                    ArrayPool<double>.Shared.Return(array.Array!);
                cache.Clear();
            }
        }
    }
}
