// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public sealed class TransformOptionsMonitor<T, TSource> : IOptionsMonitor<T>
    {
        private readonly IOptionsMonitor<TSource> _source;
        private Func<TSource, T> _transform;
        private Tuple<object, TSource, T> _value;

        public Func<TSource, T> TransformFunc
        {
            get => _transform;
            set => _transform = value;
        }

        public T CurrentValue
        {
            get
            {
                var transform = _transform;
                var sourceValue = _source.CurrentValue;
                var value = _value;
                if (!Equals(value.Item1, transform) ||
                    !TransformOptionsMonitor.IsEqual(value.Item2, sourceValue))
                    _value = value = new Tuple<object, TSource, T>(transform, sourceValue, _transform(sourceValue));
                return value.Item3;
            }
        }

        public TransformOptionsMonitor(
            IOptionsMonitor<TSource> source,
            Func<TSource, T> transform)
        {
            _source = source;
            _transform = transform;
            var sourceValue = source.CurrentValue;
            _value = new Tuple<object, TSource, T>(transform, sourceValue, _transform(sourceValue));
        }

        public T Get(string? name)
        {
            var transform = _transform;
            var sourceValue = _source.Get(name);
            var value = _value;
            if (Equals(value.Item1, transform) &&
                TransformOptionsMonitor.IsEqual(value.Item2, sourceValue))
                return value.Item3;
            else
                return _transform(sourceValue);
        }

        public IDisposable? OnChange(Action<T, string?> listener)
            => throw new NotSupportedException();
    }

    public sealed class TransformOptionsMonitor<T, TSource1, TSource2> : IOptionsMonitor<T>
    {
        private readonly IOptionsMonitor<TSource1> _source1;
        private readonly IOptionsMonitor<TSource2> _source2;
        private Func<TSource1, TSource2, T> _transform;
        private Tuple<object, TSource1, TSource2, T> _value;

        public Func<TSource1, TSource2, T> TransformFunc
        {
            get => _transform;
            set => _transform = value;
        }

        public T CurrentValue
        {
            get
            {
                var transform = _transform;
                var source1Value = _source1.CurrentValue;
                var source2Value = _source2.CurrentValue;
                var value = _value;
                if (!Equals(value.Item1, transform) ||
                    !TransformOptionsMonitor.IsEqual(value.Item2, source1Value) || 
                    !TransformOptionsMonitor.IsEqual(value.Item3, source2Value))
                    _value = value = new Tuple<object, TSource1, TSource2, T>(transform, source1Value, source2Value, _transform(source1Value, source2Value));
                return value.Item4;
            }
        }

        public TransformOptionsMonitor(
            IOptionsMonitor<TSource1> source1,
            IOptionsMonitor<TSource2> source2,
            Func<TSource1, TSource2, T> transform)
        {
            _source1 = source1;
            _source2 = source2;
            _transform = transform;
            var source1Value = source1.CurrentValue;
            var source2Value = source2.CurrentValue;
            _value = new Tuple<object, TSource1, TSource2, T>(transform, source1Value, source2Value, _transform(source1Value, source2Value));
        }

        public T Get(string? name)
        {
            var transform = _transform;
            var source1Value = _source1.Get(name);
            var source2Value = _source2.Get(name);
            var value = _value;
            if (Equals(value.Item1, transform) &&
                TransformOptionsMonitor.IsEqual(value.Item2, source1Value) &&
                TransformOptionsMonitor.IsEqual(value.Item3, source2Value))
                return value.Item4;
            else
                return _transform(source1Value, source2Value);
        }

        public IDisposable? OnChange(Action<T, string?> listener)
            => throw new NotSupportedException();
    }

    public sealed class TransformOptionsMonitor<T, TSource1, TSource2, TSource3> : IOptionsMonitor<T>
    {
        private readonly IOptionsMonitor<TSource1> _source1;
        private readonly IOptionsMonitor<TSource2> _source2;
        private readonly IOptionsMonitor<TSource3> _source3;
        private Func<TSource1, TSource2, TSource3, T> _transform;
        private Tuple<object, TSource1, TSource2, TSource3, T> _value;

        public Func<TSource1, TSource2, TSource3, T> TransformFunc
        {
            get => _transform;
            set => _transform = value;
        }

        public T CurrentValue
        {
            get
            {
                var transform = _transform;
                var source1Value = _source1.CurrentValue;
                var source2Value = _source2.CurrentValue;
                var source3Value = _source3.CurrentValue;
                var value = _value;
                if (!TransformOptionsMonitor.IsEqual(value.Item1, transform) ||
                    !TransformOptionsMonitor.IsEqual(value.Item2, source1Value) ||
                    !TransformOptionsMonitor.IsEqual(value.Item3, source2Value) ||
                    !TransformOptionsMonitor.IsEqual(value.Item4, source3Value))
                    _value = value = new Tuple<object, TSource1, TSource2, TSource3, T>(transform, source1Value, source2Value, source3Value, _transform(source1Value, source2Value, source3Value));
                return value.Item5;
            }
        }

        public TransformOptionsMonitor(
            IOptionsMonitor<TSource1> source1,
            IOptionsMonitor<TSource2> source2,
            IOptionsMonitor<TSource3> source3,
            Func<TSource1, TSource2, TSource3, T> transform)
        {
            _source1 = source1;
            _source2 = source2;
            _source3 = source3;
            _transform = transform;
            var source1Value = source1.CurrentValue;
            var source2Value = source2.CurrentValue;
            var source3Value = source3.CurrentValue;
            _value = new Tuple<object, TSource1, TSource2, TSource3, T>(transform, source1Value, source2Value, source3Value, _transform(source1Value, source2Value, source3Value));
        }

        public T Get(string? name)
        {
            var transform = _transform;
            var source1Value = _source1.Get(name);
            var source2Value = _source2.Get(name);
            var source3Value = _source3.Get(name);
            var value = _value;
            if (Equals(value.Item1, transform) &&
                TransformOptionsMonitor.IsEqual(value.Item2, source1Value) &&
                TransformOptionsMonitor.IsEqual(value.Item3, source2Value) &&
                TransformOptionsMonitor.IsEqual(value.Item4, source3Value))
                return value.Item5;
            else
                return _transform(source1Value, source2Value, source3Value);
        }

        public IDisposable? OnChange(Action<T, string?> listener)
            => throw new NotSupportedException();
    }

    public static class TransformOptionsMonitor
    {
        public static IOptionsMonitor<T> Transform<T, TSource>(this IOptionsMonitor<TSource> source, Func<TSource, T> transform)
            => new TransformOptionsMonitor<T, TSource>(source, transform);

        public static IOptionsMonitor<T> Transform<T, TSource1, TSource2>(this IOptionsMonitor<TSource1> source1, IOptionsMonitor<TSource2> source2, Func<TSource1, TSource2, T> transform)
            => new TransformOptionsMonitor<T, TSource1, TSource2>(source1, source2, transform);

        public static IOptionsMonitor<T> Transform<T, TSource1, TSource2, TSource3>(this IOptionsMonitor<TSource1> source1, IOptionsMonitor<TSource2> source2, IOptionsMonitor<TSource3> source3, Func<TSource1, TSource2, TSource3, T> transform)
            => new TransformOptionsMonitor<T, TSource1, TSource2, TSource3>(source1, source2, source3, transform);

        internal static bool IsEqual<T>(T? x, T? y)
        {
            if (typeof(T).IsValueType)
                return EqualityComparer<T>.Default.Equals(x, y);
            else
                return ReferenceEquals(x, y);
        }
    }
}
