// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Collections
{
    public readonly record struct ArenaBuffer<T>(Arena<T>? Arena, int Offset, int Length)
    {
        public ArraySegment<T> Segment => Arena != null ? new ArraySegment<T>(Arena.TotalBuffer, Offset, Length) : default;
        public Span<T> Span => Arena != null ? Arena.TotalBuffer.AsSpan(Offset, Length) : Span<T>.Empty;
        public Memory<T> Memory => Arena != null ? Arena.TotalBuffer.AsMemory(Offset, Length) : Memory<T>.Empty;

        public void AddReference()
            => Arena?.Allocator.AddReference(this);

        public void DecrementReference()
            => Arena?.Allocator.DecrementReference(this);
    }
}
