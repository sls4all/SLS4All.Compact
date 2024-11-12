// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public sealed class WeakConcurrentDictionary<TKey, TValue> : IEnumerable<TValue>
        where TKey: notnull
        where TValue: class
    {
        private readonly ConcurrentDictionary<TKey, WeakReference<TValue>> _values = new();

        public void Add(TKey key, TValue value)
        {
            _values.TryAdd(key, new WeakReference<TValue>(value));
        }

        public void Remove(TKey key)
        {
            _values.TryRemove(key, out _);
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
        {
            if (_values.TryGetValue(key, out var reference))
            {
                if (reference.TryGetTarget(out value))
                    return true;
                _values.TryRemove(key, out _);
            }
            else
                value = default;
            return false;
        }

        public IEnumerator<TValue> GetEnumerator()
        {
            foreach (var pair in _values)
            {
                if (pair.Value.TryGetTarget(out var value))
                    yield return value;
                else
                    _values.TryRemove(pair.Key, out _);
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
