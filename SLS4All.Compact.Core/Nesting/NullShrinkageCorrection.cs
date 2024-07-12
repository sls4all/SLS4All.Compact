// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Numerics;
using SLS4All.Compact.Temperature;

namespace SLS4All.Compact.Nesting
{
    public sealed class NullShrinkageCorrection : IShrinkageCorrection
    {
        public static NullShrinkageCorrection Instance { get; } = new();
        public Vector3 Scale => Vector3.One;
    }
}
