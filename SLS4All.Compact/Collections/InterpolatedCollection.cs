// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public sealed class InterpolatedCollection
    {
        private readonly PrimitiveList<(double Key, double Value, int Index)> _values;

        public PrimitiveList<(double Key, double Value, int Index)> Values => _values;
        public bool HasData => _values.Count != 0;

        public InterpolatedCollection(IEnumerable<(double Key, double Value, int Index)>? input = null)
        {
            _values = new PrimitiveList<(double, double, int)>();
            if (input != null)
            {
                foreach (var item in input)
                    _values.Add() = item;
            }
        }

        public int GetMaxIndex()
        {
            var values = _values.Span;
            var res = 0;
            for (int i = 0; i < values.Length; i++)
            {
                ref var item = ref values[i];
                if (item.Index > res)
                    res = item.Index;
            }
            return res;
        }

        public bool Contains(double key)
        {
            var values = _values.Span;
            var at = values.BinarySearch((key, double.MinValue, int.MinValue));
            if (at < 0)
                at = ~at;
            return at < values.Length && values[at].Key == key;
        }

        public bool Remove(double key)
        {
            var values = _values.Span;
            var at = values.BinarySearch((key, double.MinValue, int.MinValue));
            if (at < 0)
                at = ~at;
            if (at < _values.Count && _values[at].Key == key)
            {
                _values.RemoveAt(at);
                return true;
            }
            else
                return false;
        }

        public bool Remove(double key, double value, int index)
        {
            var at = _values.Span.BinarySearch((key, value, index));
            if (at >= 0)
            {
                _values.RemoveAt(at);
                return true;
            }
            else
                return false;
        }

        public (double? Value, double Distance, bool Extrapolated) TryGetValue(double key)
        {
            if (key <= 0) // cant allow non-positive values due to extrapolation performed
                return (0, 0, false);
            var values = _values.Span;
            if (values.Length == 0)
                return (null, double.MaxValue, false);
            var at = values.BinarySearch((key, double.MinValue, int.MinValue));
            if (at < 0)
                at = ~at;
            if (at >= values.Length)
            {
                ref var last = ref values[at - 1];
                Debug.Assert(last.Key <= key);
                var factor = key / last.Key;
                return (last.Value * factor, key - last.Key, true);
            }
            else if (at == 0)
            {
                ref var first = ref values[0];
                Debug.Assert(first.Key >= key);
                var factor = key / first.Key;
                return (first.Value * factor, first.Key - key, true);
            }
            else
            {
                ref var a = ref values[at - 1];
                ref var b = ref values[at];
                Debug.Assert(a.Key <= key);
                Debug.Assert(b.Key >= key);
                var dist = b.Key - a.Key;
                if (dist < 0.001 || a.Key == key)
                    return (a.Value, 0, false);
                else
                {
                    var factor = (key - a.Key) / dist;
                    return (a.Value * (1 - factor) + b.Value * factor, Math.Min(key - a.Key, b.Key - key), false);
                }
            }
        }

        public void Add(double key, double value, int index)
        {
            if (key <= 0) // cant allow non-positive values due to extrapolation performed
                return;
            var item = (key, value, index);
            var at = _values.Span.BinarySearch(item);
            if (at < 0)
                at = ~at;
            _values.Insert(at, item);
        }

       public InterpolatedCollection Clone()
            => new InterpolatedCollection(_values);

        public void ReduceCount(int maxCount)
        {
            if (_values.Count <= maxCount)
                return;
            var random = new Random();
            var output = new List<(double weight, double latency, int printIndex)>();
            var startAddingAt = _values.Count - maxCount;
            var index = 0;
            foreach (var item in _values.OrderBy(x => x.Index).ThenBy(x => random.NextDouble()))
            {
                if (index++ >= startAddingAt)
                    output.Add(item);
            }
            _values.Clear();
            foreach (var item in output.Order())
                _values.Add(item);
        }

        public void Clear()
        {
            _values.Clear();
        }
    }
}
