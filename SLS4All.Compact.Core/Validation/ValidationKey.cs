// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Diagnostics.CodeAnalysis;

namespace SLS4All.Compact.Validation
{
    public readonly struct ValidationKey : IEquatable<ValidationKey>
    {
        public object Obj { get; }
        public string Path { get; }
        public ValidationKey(
            object obj,
            string path)
        {
            Obj = obj;
            Path = path;
        }

        public bool Equals(ValidationKey other)
            => ReferenceEquals(Obj, other.Obj) && Path == other.Path;

        public override bool Equals([NotNullWhen(true)] object? obj)
            => obj is ValidationKey other && Equals(other);

        public override int GetHashCode()
            => ReferenceEqualityComparer.Instance.GetHashCode(Obj) ^ Path.GetHashCode();
    }
}
