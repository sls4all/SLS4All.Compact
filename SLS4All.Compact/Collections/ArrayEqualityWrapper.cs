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
    public readonly struct ArrayEqualityWrapper<T>(T[] array) : IEquatable<ArrayEqualityWrapper<T>>
    {
        public T[] Array { get; } = array;

        public bool Equals(ArrayEqualityWrapper<T> other)
            => ArrayEqualityComparer<T>.Instance.Equals(Array, other.Array);

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is ArrayEqualityWrapper<T> other && Equals(other);

        public override int GetHashCode()
            => ArrayEqualityComparer<T>.Instance.GetHashCode(Array);

        public static implicit operator ArrayEqualityWrapper<T>(T[] array)
            => new ArrayEqualityWrapper<T>(array);

        public static implicit operator T[](ArrayEqualityWrapper<T> obj)
            => obj.Array;
    }
}
