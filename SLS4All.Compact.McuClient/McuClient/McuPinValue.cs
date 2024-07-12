// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public readonly record struct McuPinValue
    {
        public float Single { get; }

        public bool IsNonZero => Single != 0;

        public bool IsFuzzyEnabled => Single >= 0.5f;

        public McuPinValue(float single)
            => Single = Math.Clamp(single, 0, 1);

        public McuPinValue Inverted
            => new McuPinValue(1.0f - Single);

        public static implicit operator McuPinValue(bool value)
            => new McuPinValue(value ? 1 : 0);

        public static implicit operator McuPinValue(float value)
            => new McuPinValue(value);

        public McuPinValue Get(bool invert)
            => invert ? Inverted : this;

        public override string ToString()
            => Single.ToString();
    }
}
