// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿#define DEBUG_TRACK
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace SLS4All.Compact.Collections
{
    public sealed class Arena<T>
    {
#if DEBUG_TRACK
        private readonly Dictionary<ArenaBuffer<T>, int> _references = new();
#endif
        private readonly ArenaAllocator<T> _allocator;
        private readonly T[] _totalBuffer;
        private volatile int _freePos;
        private volatile int _referenceCount;

        public ArenaAllocator<T> Allocator => _allocator;
        public T[] TotalBuffer => _totalBuffer;
        public bool IsFull => _freePos >= _totalBuffer.Length;
        public bool IsFree => _referenceCount == 0 && _freePos >= _totalBuffer.Length;
        public int ReferenceCount => _referenceCount;

        public Arena(ArenaAllocator<T> allocator, T[] totalBuffer)
        {
            _allocator = allocator;
            _totalBuffer = totalBuffer;
        }

        public bool TryAllocate(int length, out ArenaBuffer<T> buffer)
        {
            Debug.Assert(length >= 0);
            var newFreePos = Interlocked.Add(ref _freePos, length);
            if (newFreePos <= _totalBuffer.Length)
            {
                Interlocked.Increment(ref _referenceCount);
                buffer = new ArenaBuffer<T>(this, newFreePos - length, length);
#if DEBUG_TRACK
                lock (_references)
                {
                    ref var refs = ref CollectionsMarshal.GetValueRefOrAddDefault(_references, buffer, out _);
                    Debug.Assert(refs == 0);
                    refs++;
                }
#endif
                return true;
            }
            else
            {
                buffer = default;
                return false;
            }
        }

        internal void ResetInternal()
        {
            _freePos = 0;
            _referenceCount = 0;
        }

        internal void AddReferenceInternal(in ArenaBuffer<T> buffer)
        {
            var newReferences = Interlocked.Increment(ref _referenceCount);
            if (newReferences == 1 && _freePos >= _totalBuffer.Length)
                throw new InvalidOperationException("Added rerefence to freed Arena");
#if DEBUG_TRACK
            lock (_references)
            {
                ref var refs = ref CollectionsMarshal.GetValueRefOrAddDefault(_references, buffer, out _);
                Debug.Assert(refs > 0);
                refs++;
            }
#endif
        }

        internal bool DecrementReferenceInternal(in ArenaBuffer<T> buffer)
        {
#if DEBUG_TRACK
            lock (_references)
            {
                ref var refs = ref CollectionsMarshal.GetValueRefOrAddDefault(_references, buffer, out _);
                Debug.Assert(refs > 0);
                if (--refs == 0)
                    _references.Remove(buffer);
            }
#endif

            var newReferences = Interlocked.Decrement(ref _referenceCount);
            if (newReferences < 0)
                throw new InvalidOperationException("Freed more items than allocated");
            return newReferences == 0 && _freePos >= _totalBuffer.Length;
        }
    }
}
