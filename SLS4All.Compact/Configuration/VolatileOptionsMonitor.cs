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
    public sealed class VolatileOptionsMonitor<T> : IOptionsMonitor<T>
        where T: class
    {
        private volatile T _currentValue;

        public T CurrentValue
        {
            get => _currentValue;
            set => _currentValue = value;
        }

        public VolatileOptionsMonitor(T value)
            => _currentValue = value;

        public T Get(string? name)
            => string.IsNullOrEmpty(name) ? CurrentValue : throw new NotSupportedException();

        public IDisposable OnChange(Action<T, string> listener)
            => NullDisposable.Instance;
    }

    public static class VolatileOptionsMonitor
    {
        public static VolatileOptionsMonitor<T> Create<T>(T value)
            where T : class
            => new VolatileOptionsMonitor<T>(value);
    }
}
