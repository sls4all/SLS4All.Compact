// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public readonly struct ValueByReference<T> : IEquatable<ValueByReference<T>>
        where T : class?
    {
        public T Value { get; }
        public ValueByReference(T value)
            => Value = value;
        public bool Equals(ValueByReference<T> other)
            => ReferenceEquals(this.Value, other.Value);
        public override int GetHashCode()
            => ReferenceEqualityComparer.Instance.GetHashCode(this.Value);
        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is ValueByReference<T> other && Equals(other);
    }

    public static class ValueByReference
    {
        public static ValueByReference<T> Create<T>(T value) where T : class?
            => new ValueByReference<T>(value);
    }
}
