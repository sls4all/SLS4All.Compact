// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public static class PrinterMemoryExtensions
    {
        public static Memory<T> BorrowArrayMemory<T>(int size)
        {
            if (size == 0)
                return Memory<T>.Empty;
            var array = ArrayPool<T>.Shared.Rent(size);
            return new Memory<T>(array, 0, size);
        }

        public static void ReturnArrayMemory<T>(in ReadOnlyMemory<T> memory)
        {
            if (MemoryMarshal.TryGetArray(memory, out var segment))
                ArrayPool<T>.Shared.Return(segment.Array!);
        }

        public static Memory<T> ToBorrowedArrayMemory<T>(this Span<T> span)
        {
            if (span.IsEmpty)
                return Memory<T>.Empty;
            var memory = BorrowArrayMemory<T>(span.Length);
            span.CopyTo(memory.Span);
            return memory;
        }

        public static Span<byte> AsSpan(this MemoryStream stream)
            => new Span<byte>(stream.GetBuffer(), 0, checked((int)stream.Length));

        public static Memory<byte> AsMemory(this MemoryStream stream)
            => new Memory<byte>(stream.GetBuffer(), 0, checked((int)stream.Length));

        public static Memory<byte> ToBorrowedArrayMemory(this MemoryStream stream)
            => stream.AsSpan().ToBorrowedArrayMemory();
    }
}
