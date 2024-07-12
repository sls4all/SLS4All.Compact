// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public sealed class TransformOptionsMonitor<T, TSource> : IOptionsMonitor<T>
        where T: class
    {
        private readonly IOptionsMonitor<TSource> _source;
        private readonly Func<TSource, T> _transform;
        private Tuple<TSource, T> _value;

        public T CurrentValue
        {
            get
            {
                var sourceValue = _source.CurrentValue;
                var value = _value;
                if (!ReferenceEquals(value.Item1, sourceValue))
                    _value = value = new Tuple<TSource, T>(sourceValue, _transform(sourceValue));
                return value.Item2;
            }
        }

        public TransformOptionsMonitor(
            IOptionsMonitor<TSource> source,
            Func<TSource, T> transform)
        {
            _source = source;
            _transform = transform;
            var sourceValue = source.CurrentValue;
            _value = new Tuple<TSource, T>(sourceValue, _transform(sourceValue));
        }

        public T Get(string? name)
        {
            var sourceValue = _source.Get(name);
            var value = _value;
            if (ReferenceEquals(value.Item1, sourceValue))
                return value.Item2;
            else
                return _transform(sourceValue);
        }

        public IDisposable? OnChange(Action<T, string?> listener)
            => throw new NotSupportedException();
    }

    public static class TransformOptionsMonitor
    {
        public static TransformOptionsMonitor<T, TSource> Create<T, TSource>(IOptionsMonitor<TSource> source, Func<TSource, T> transform)
            where T: class
            => new TransformOptionsMonitor<T, TSource>(source, transform);
    }
}
