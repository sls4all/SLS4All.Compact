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
    public sealed class ArraySegmentEqualityComparer<T> : IEqualityComparer<ArraySegment<T>>
    {
        public static ArraySegmentEqualityComparer<T> Instance { get; } = new();

        public bool Equals(ArraySegment<T> x, ArraySegment<T> y)
        {
            if (x.Count != y.Count)
                return false;
            return x.AsSpan().SequenceEqual(y);
        }

        public int GetHashCode([DisallowNull] ArraySegment<T> obj)
        {
            var hash = new HashCode();
            for (int i = 0; i < obj.Count; i++)
                hash.Add(obj[i]);
            return hash.ToHashCode();
        }
    }
}
