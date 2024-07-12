// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using SLS4All.Compact.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public sealed class ConstantOptionsMonitor<T> : IOptionsMonitor<T>
    {
        public T CurrentValue { get; set; }

        public ConstantOptionsMonitor(T value)
            => CurrentValue = value;

        public T Get(string? name)
            => throw new KeyNotFoundException();

        public IDisposable OnChange(Action<T, string> listener)
            => NullDisposable.Instance;
    }

    public static class ConstantOptionsMonitor
    {
        public static ConstantOptionsMonitor<T> Create<T>(T value)
            => new ConstantOptionsMonitor<T>(value);
    }
}
