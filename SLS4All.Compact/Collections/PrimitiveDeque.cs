// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;

namespace SLS4All.Compact.Collections
{
    /// <summary>
    /// Implementation of a queue/stack hybrid
    /// </summary>
    public sealed class PrimitiveDeque<T> : IEnumerable<T>, IReadOnlyList<T>
    {
        private const int _minArrayLength = 16; // items

        private T[] _items;
        /// <summary>
        /// Index of: 
        /// <para>First item to be returned from <see cref="PopFront"/></para>
        /// <para>First item to be returned from <see cref="PeekFront"/></para>
        /// <para>Last item to be returned from <see cref="PeekBack"/></para>
        /// <para>Last to be returned from <see cref="PopBack"/></para>
        /// </summary>
        private int _head;
        private int _count;

        /// <summary>
        /// Returns a reference to specified item starting with the <see cref="PeekFront"/>/<see cref="PopFront"/> top, 
        /// and ending with the last enqueued.
        /// </summary>
        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index > _count)
                    ThrowArgumentOutOfRangeIndex();
                var pos = (_head + index) % _items.Length;
                return ref _items[pos];
            }
        }

        /// <summary>
        /// Returns o specified item starting with the <see cref="PeekFront"/>/<see cref="PopFront"/> top, 
        /// and ending with the last enqueued.
        /// </summary>
        T IReadOnlyList<T>.this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (index < 0 || index > _count)
                    ThrowArgumentOutOfRangeIndex();
                var pos = (_head + index) % _items.Length;
                return _items[pos];
            }
        }

        /// <summary>
        /// Gets the number of items in the queue
        /// </summary>
        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _count;
        }

        public PrimitiveDeque(int capacity = 0)
        {
            _items = capacity != 0 ? new T[capacity] : Array.Empty<T>();
            _head = 0;
            _count = 0;
        }

        public void Clear()
        {
            _head = 0;
            _count = 0;
        }

        /// <summary>
        /// Enqueues an item and returns reference to it (pushes new item to the back)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T PushBack()
        {
            if (_count >= _items.Length)
                IncreaseSize();
            var pos = (_head + _count) % _items.Length;
            ref var item = ref _items[pos];
            _count++;
            return ref item!;
        }

        /// <summary>
        /// Enqueues an item  (pushes new item to the back)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushBack(T value)
            => PushBack() = value;

        /// <summary>
        /// Pushes an item to the start of the queue (will be dequeued at next call of <see cref="PopFront"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T PushFront()
        {
            if (_count >= _items.Length)
                IncreaseSize();
            var pos = --_head;
            if (pos < 0)
                _head = pos = _items.Length - 1;
            ref var item = ref _items[pos];
            _count++;
            return ref item!;
        }

        /// <summary>
        /// Pushes an item to the start of the queue (will be dequeued at next call of <see cref="PopFront"/>
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PushFront(T value)
            => PushFront() = value;

        /// <summary>
        /// Removes the last enqueued item and returns reference to it.
        /// Item is not cleared.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T PopBack()
        {
            if (_count == 0)
                ThrowQueueEmpty();
            Debug.Assert(_count > 0);
            var pos = (_head + _count - 1) % _items.Length;
            _count--;
            return ref _items[pos];
        }

        /// <summary>
        /// Removes the last enqueued item and returns reference to it.
        /// Item is not cleared.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PopBack(out T value)
            => value = PopBack();

        /// <summary>
        /// Removes first item and returns reference to it (pops item from the front)
        /// Item is not cleared.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T PopFront()
        {
            if (_count == 0)
                ThrowQueueEmpty();
            Debug.Assert(_count > 0);
            ref var item = ref _items[_head];
            if (++_head >= _items.Length)
                _head = 0;
            _count--;
            return ref item!;
        }

        /// <summary>
        /// Removes number of items. Items are not cleared.
        /// </summary>
        public void PopFront(int count)
        {
            while (count-- > 0)
                PopFront();
        }

        /// <summary>
        /// Removes at max specified number of first items (pops items from the front)
        /// Item is not cleared.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int PopFront(Span<T> target)
        {
            var total = 0;
            while (true)
            {
                if (target.Length == 0 || _count == 0)
                    return total;
                Debug.Assert(_count > 0);
                var head = _head;
                var len = _items.Length - head;
                if (len > target.Length)
                    len = target.Length;
                if ((_head += len) >= _items.Length)
                    _head = 0;
                _count -= len;
                _items.AsSpan(head, len).CopyTo(target);
                target = target.Slice(len);
                total += len;
            }
        }

        /// <summary>
        /// Copies at max specified number of first items (pops items from the front)
        /// Item is not cleared.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CopyTo(Span<T> target)
        {
            var total = 0;
            var head_ = _head;
            var count_ = _count;
            while (true)
            {
                if (target.Length == 0 || count_ == 0)
                    return total;
                Debug.Assert(count_ > 0);
                var head = head_;
                var len = _items.Length - head;
                if (len > target.Length)
                    len = target.Length;
                if ((head_ += len) >= _items.Length)
                    head_ = 0;
                count_ -= len;
                _items.AsSpan(head, len).CopyTo(target);
                target = target.Slice(len);
                total += len;
            }
        }

        /// <summary>
        /// Removes first item and returns reference to it (pops item from the front)
        /// Item is not cleared.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PopFront(out T value)
            => value = PopFront();

        /// <summary>
        /// Returns reference to the first/front item (to be dequeued)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T PeekFront()
        {
            if (_count == 0)
                ThrowQueueEmpty();
            Debug.Assert(_count > 0);
            return ref _items[_head];
        }

        /// <summary>
        /// Returns reference to the last/back item (just enqueued)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PeekFront(out T value)
            => value = PeekFront();

        /// <summary>
        /// Returns reference to the last/back item (just enqueued)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ref T PeekBack()
        {
            if (_count == 0)
                ThrowQueueEmpty();
            Debug.Assert(_count > 0);
            var pos = (_head + _count) % _items.Length;
            return ref _items[pos];
        }

        /// <summary>
        /// Returns reference to the last/back item (just enqueued)
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void PeekBack(out T value)
            => value = PeekBack();

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowQueueEmpty()
        {
            throw new InvalidOperationException("Queue is empty");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ThrowArgumentOutOfRangeIndex()
        {
            throw new ArgumentOutOfRangeException("index");
        }

        private static int GetPower(int v)
        {
            int r = 0;
            while (v != 0)
            {
                v >>= 1;
                r++;
            }
            return r;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void IncreaseSize()
        {
            var oldSize = _items.Length;
            var minSize = oldSize + 1;
            if (minSize < _minArrayLength)
                minSize = _minArrayLength;
            var newSize = (int)BitOperations.RoundUpToPowerOf2((uint)minSize);
            Debug.Assert(_count == oldSize);
            var oldItems = _items;
            var newItems = new T[newSize];
            var end = _head + _count;
            if (end < oldSize)
            {
                Array.Copy(oldItems, _head, newItems, 0, _count);
            }
            else
            {
                var copyLength = oldSize - _head;
                Array.Copy(oldItems, _head, newItems, 0, copyLength);
                Array.Copy(oldItems, 0, newItems, copyLength, _count - copyLength);
            }
            _items = newItems;
            _head = 0;
        }

        public IEnumerator<T> GetEnumerator()
        {
            for (int i = 0; i < Count; i++)
                yield return this[i];
        }

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }
}
