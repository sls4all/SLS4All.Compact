// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public readonly struct ArraySegmentEqualityWrapper<T>(ArraySegment<T> segment) : IEquatable<ArraySegmentEqualityWrapper<T>>
    {
        public ArraySegment<T> Segment { get; } = segment;

        public bool Equals(ArraySegmentEqualityWrapper<T> other)
            => ArraySegmentEqualityComparer<T>.Instance.Equals(Segment, other.Segment);

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is ArrayEqualityWrapper<T> other && Equals(other);

        public override int GetHashCode()
            => ArraySegmentEqualityComparer<T>.Instance.GetHashCode(Segment);

        public static implicit operator ArraySegmentEqualityWrapper<T>(ArraySegment<T> segment)
            => new ArraySegmentEqualityWrapper<T>(segment);

        public static implicit operator ArraySegment<T>(ArraySegmentEqualityWrapper<T> obj)
            => obj.Segment;
    }
}
