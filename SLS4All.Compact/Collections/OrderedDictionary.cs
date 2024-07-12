// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public class OrderedDictionary<TKey, TValue> 
        : IDictionary<TKey, TValue>
        , IReadOnlyDictionary<TKey, TValue>
        , ICollection<KeyValuePair<TKey, TValue>>
        , IEnumerable<KeyValuePair<TKey, TValue>>, IEnumerable
        where TKey: notnull
    {
        private readonly List<TKey> _orderedKeys;
        private readonly Dictionary<TKey, TValue> _dictionary;

        public TValue this[TKey key]
        {
            get
            {
                return _dictionary[key];
            }
            set
            {
                _dictionary[key] = value;
                if (!_orderedKeys.Contains(key))
                    _orderedKeys.Add(key);
            }
        }

        public ICollection<TKey> Keys => _orderedKeys;
        public ICollection<TValue> Values => _orderedKeys.Select((TKey x) => _dictionary[x]).ToList();
        public int Count => _dictionary.Count;
        public bool IsReadOnly => ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).IsReadOnly;

        IEnumerable<TKey> IReadOnlyDictionary<TKey, TValue>.Keys => _orderedKeys;

        IEnumerable<TValue> IReadOnlyDictionary<TKey, TValue>.Values => _orderedKeys.Select((TKey x) => _dictionary[x]);

        public OrderedDictionary()
        {
            _dictionary = new Dictionary<TKey, TValue>();
            _orderedKeys = new List<TKey>();
        }

        public OrderedDictionary(int capacity)
        {
            _dictionary = new Dictionary<TKey, TValue>(capacity);
            _orderedKeys = new List<TKey>(capacity);
        }

        public OrderedDictionary(IEqualityComparer<TKey> comparer)
        {
            _dictionary = new Dictionary<TKey, TValue>(comparer);
            _orderedKeys = new List<TKey>();
        }

        public OrderedDictionary(int capacity, IEqualityComparer<TKey> comparer)
        {
            _dictionary = new Dictionary<TKey, TValue>(capacity, comparer);
            _orderedKeys = new List<TKey>();
        }

        public OrderedDictionary(IDictionary<TKey, TValue> dictionary, IEqualityComparer<TKey> comparer)
        {
            this._dictionary = new Dictionary<TKey, TValue>(dictionary, comparer);
            _orderedKeys = new List<TKey>();
        }

        public TKey GetKeyByIndex(int index)
            => _orderedKeys[index];

        public TValue GetValueByIndex(int index)
            => this[GetKeyByIndex(index)];

        public void Add(TKey key, TValue value)
        {
            _dictionary.Add(key, value);
            _orderedKeys.Add(key);
        }

        public void Add(KeyValuePair<TKey, TValue> item)
        {
            ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Add(item);
            _orderedKeys.Add(item.Key);
        }

        public void Clear()
        {
            _dictionary.Clear();
            _orderedKeys.Clear();
        }

        public bool Contains(KeyValuePair<TKey, TValue> item)
            => ((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Contains(item);

        public bool ContainsKey(TKey key)
            => _dictionary.ContainsKey(key);

        public void CopyTo(KeyValuePair<TKey, TValue>[] array, int arrayIndex)
        {
            using var enumerator = GetEnumerator();
            while (enumerator.MoveNext())
            {
                var current = enumerator.Current;
                array[arrayIndex++] = current;
            }
        }

        public IEnumerator<KeyValuePair<TKey, TValue>> GetEnumerator()
        {
            foreach (TKey item in _orderedKeys)
            {
                yield return new KeyValuePair<TKey, TValue>(item, _dictionary[item]);
            }
        }

        public bool Remove(TKey key)
        {
            if (_dictionary.Remove(key))
            {
                _orderedKeys.Remove(key);
                return true;
            }

            return false;
        }

        public bool Remove(KeyValuePair<TKey, TValue> item)
        {
            if (((ICollection<KeyValuePair<TKey, TValue>>)_dictionary).Remove(item))
            {
                _orderedKeys.Remove(item.Key);
                return true;
            }

            return false;
        }

        public bool TryGetValue(TKey key, [MaybeNullWhen(false)] out TValue value)
            => ((IDictionary<TKey, TValue>)_dictionary).TryGetValue(key, out value);

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
