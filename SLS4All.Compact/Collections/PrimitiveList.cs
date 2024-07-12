// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Buffers;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SLS4All.Compact.Collections
{
    internal static class PrimitiveList
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        [StackTraceHidden]
        internal static void ThrowIndexOutOfRangeException()
            => throw new ArgumentOutOfRangeException("index");

        [MethodImpl(MethodImplOptions.NoInlining)]
        [StackTraceHidden]
        internal static void ThrowStackAllocOutOfRangeException()
            => throw new ArgumentOutOfRangeException("stackAlloc");
    }

    /// <summary>
    /// Simpler and faster alternative to <see cref="List{T}"/> to use in inner algorithms.
    /// Does without runtime bounds checking, only asserts in Debug. Use very carefully.
    /// </summary>
    [DebuggerDisplay("Count = {Count}")]
    public sealed unsafe class PrimitiveList<T> : IEnumerable<T>, IReadOnlyList<T>
    {
        private const int _minArrayLength = 16; // items

        private T[] _array;
        private int _count;

        public Span<T> Span
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get =>  _array.AsSpan(0, _count);
        }

        public Memory<T> Memory
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _array.AsMemory(0, _count);
        }

        public ArraySegment<T> Segment
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new ArraySegment<T>(_array, 0, _count);
        }

        public ReadOnlySequence<T> Sequence
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => new ReadOnlySequence<T>(_array, 0, _count);
        }

        public T[] InnerArray
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _array;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
            set
            {
                Debug.Assert(value >= 0);
                if (_array.Length < value)
                    ResizeArray(value);
                _count = value;
            }
        }

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(index >= 0 && index < _count);
                return ref _array[index];
            }
        }

        T IReadOnlyList<T>.this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                Debug.Assert(index >= 0 && index < _count);
                return _array[index];
            }
        }

        public PrimitiveList(int capacity = 0, int count = 0)
        {
            if (capacity < count)
                capacity = (int)BitOperations.RoundUpToPowerOf2((uint)count);
            if (capacity == 0)
                _array = Array.Empty<T>();
            else
                _array = new T[capacity];
            _count = count;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Add(T item)
        {
            var index = _count;
            if (index >= _array.Length)
                ResizeArray(index + 1);
            _array[index] = item;
            _count++;
        }

        public void AddRange(ReadOnlySpan<T> span)
        {
            if (span.Length == 0)
                return;
            var prevCount = _count;
            Count = prevCount + span.Length;
            span.CopyTo(_array.AsSpan(prevCount));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T Add()
        {
            var index = _count;
            if (index >= _array.Length)
                ResizeArray(index + 1);
            _count++;
            return ref _array[index];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T AddItems(int count)
        {
            var index = _count;
            if (index + count > _array.Length)
                ResizeArray(index + count);
            _count += count;
            return ref _array[index];
        }

        public void RemoveAt(int index)
        {
            if (index < 0 || index >= _count)
                PrimitiveList.ThrowIndexOutOfRangeException();
            if (index != _count - 1)
                Array.Copy(_array, index + 1, _array, index, _count - index - 1);
            _count--;
        }

        public void RemoveRange(int index, int count)
        {
            if (index < 0 || count < 0 || index + count > _count)
                PrimitiveList.ThrowIndexOutOfRangeException();
            if (index != _count - count)
                Array.Copy(_array, index + count, _array, index, _count - index - count);
            _count -= count;
        }

        public bool Contains(T value)
            => IndexOf(value) != -1;

        public int IndexOf(T value)
        {
            if (_count > 0)
                return Array.IndexOf(_array, value, 0, _count);
            return -1;
        }

        public void DistinctInplace()
        {
            for (int i1 = _count - 1; i1 >= 0; i1--)
            {
                ref var v1 = ref this[i1];
                for (int i2 = i1 - 1; i2 >= 0; i2--)
                {
                    ref var v2 = ref this[i2];
                    if (EqualityComparer<T>.Default.Equals(v1, v2))
                    {
                        RemoveAt(i1);
                        break;
                    }
                }
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ResizeArray(int minSize)
        {
            if (minSize < _minArrayLength)
                minSize = _minArrayLength;
            var newSize = (int)BitOperations.RoundUpToPowerOf2((uint)minSize);
            Array.Resize(ref _array, newSize);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public void Clear()
        {
            _count = 0;
        }

        public T[] ToArray()
        {
            var array = _count == 0 ? Array.Empty<T>() : new T[_count];
            Array.Copy(_array, 0, array, 0, _count);
            return array;
        }

        public void RemoveFromBeginning(int count)
        {
            if (count == _count)
            {
                _count = 0;
                return;
            }
            Span.Slice(count).CopyTo(Span);
            Count -= count;
        }

        public void CopyFrom(Span<T> span)
        {
            Clear();
            AddRange(span);
        }

        public PrimitiveList<T> Clone()
        {
            var res = new PrimitiveList<T>(_count);
            res.AddRange(Span);
            return res;
        }

        public Span<T>.Enumerator GetEnumerator()
            => Span.GetEnumerator();

        IEnumerator<T> IEnumerable<T>.GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
            => ((IEnumerable<T>)this).GetEnumerator();

        public ref T Insert(int first)
        {
            var prevCount = Count++;
            var span = Span;
            span.Slice(first, prevCount - first).CopyTo(span.Slice(first + 1));
            return ref span[first];
        }

        public ref T Insert(int first, int count)
        {
            Debug.Assert(count >= 0);
            var prevCount = Count + count;
            var span = Span;
            span.Slice(first, prevCount - first).CopyTo(span.Slice(first + count));
            return ref span[first];
        }

        public void InsertRange(int first, Span<T> items)
        {
            Insert(first, items.Length);
            items.CopyTo(Span.Slice(first));
        }

        public void Insert(int first, T value)
            => Insert(first) = value;
    }
}
