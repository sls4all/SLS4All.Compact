// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public readonly ref struct StrideSpan<T>
    {
        public ref struct Enumerator
        {
            private readonly StrideSpan<T> _span;
            private int _index;

            public ref T Current
            {
                [MethodImpl(MethodImplOptions.AggressiveInlining)]
                get => ref _span[_index];
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            internal Enumerator(StrideSpan<T> span)
            {
                _span = span;
                _index = -1;
            }

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            public bool MoveNext()
            {
                int index = _index + 1;
                if (index < _span.Length)
                {
                    _index = index;
                    return true;
                }

                return false;
            }
        }

        private readonly nint _stride;
        private readonly ref T _reference;
        private readonly int _length;

        public ref T this[int index]
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if ((uint)index >= (uint)_length)
                    ThrowIndexOutOfRangeException();
                return ref Unsafe.AddByteOffset(ref _reference, (nint)(uint)index * _stride);
            }
        }

        public int Length
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _length;
        }

        public static StrideSpan<T> Empty => default;

        public bool IsEmpty
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]  
            get => _length == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public StrideSpan(ref T reference, nint stride, int length)
        {
            _reference = ref reference;
            _stride = stride;
            _length = length;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowIndexOutOfRangeException()
            => throw new ArgumentOutOfRangeException("index");

        public override string ToString()
            => $"{nameof(StrideSpan)}<{typeof(T).Name}>[{_length}]";

        public Enumerator GetEnumerator() => new Enumerator(this);

        public StrideSpan<T> Slice(int start)
        {
            if ((uint)start > (uint)_length)
                ThrowIndexOutOfRangeException();
            return new StrideSpan<T>(
                ref Unsafe.AddByteOffset(ref _reference, (nint)(uint)start * _stride),
                _stride,
                _length - start);
        }

        public StrideSpan<T> Slice(int start, int length)
        {
            if ((ulong)(uint)start + (ulong)(uint)length > (ulong)(uint)_length)
                ThrowIndexOutOfRangeException();
            return new StrideSpan<T>(
                ref Unsafe.AddByteOffset(ref _reference, (nint)(uint)start * _stride),
                _stride,
                length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator StrideSpan<T>(Span<T> source)
            => new StrideSpan<T>(ref MemoryMarshal.GetReference(source), Unsafe.SizeOf<T>(), source.Length);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator StrideSpan<T>(T[] source)
            => source.AsSpan();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator StrideSpan<T>(ArraySegment<T> source)
            => source.AsSpan();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static implicit operator StrideSpan<T>(Memory<T> source)
            => source.Span;
    }

    public interface IStrideSpanReferenceGetter<TSource, T>
    {
        static abstract ref T GetReference(ref TSource source);
    }

    public static class StrideSpan
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StrideSpan<T> AsStrideSpan<T>(this Span<T> source)
            => source;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StrideSpan<T> AsStrideSpan<T>(this T[] source)
            => source;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StrideSpan<T> AsStrideSpan<T>(this ArraySegment<T> source)
            => source;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static StrideSpan<T> AsStrideSpan<T>(this Memory<T> source)
            => source;

        public static StrideSpan<T> AsStrideSpan<TSource, T, TGetter>(this Span<TSource> source)
            where TGetter: IStrideSpanReferenceGetter<TSource, T>
        {
            if (source.IsEmpty)
                return StrideSpan<T>.Empty;
            ref var firstSource = ref MemoryMarshal.GetReference(source);
            ref var first = ref TGetter.GetReference(ref firstSource);
            var offset = Unsafe.ByteOffset(ref Unsafe.As<TSource, T>(ref firstSource), ref first);
            if (offset < 0 || offset >= Unsafe.SizeOf<TSource>())
                throw new ArgumentOutOfRangeException(nameof(first));
            return new StrideSpan<T>(ref first, Unsafe.SizeOf<TSource>(), source.Length);
        }
    }
}
