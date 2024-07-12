// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public static class StreamExtensions
    {
        public static T ReadBytes<T>(this Stream stream)
            where T : struct
        {
            T value = default;
            stream.ReadExactly(MemoryMarshal.Cast<T, byte>(new Span<T>(ref value)));
            return value;
        }

        public static void ReadBytes<T>(this Stream stream, ref T value)
            where T: struct
        {
            stream.ReadExactly(MemoryMarshal.Cast<T, byte>(new Span<T>(ref value)));
        }

        public static void ReadBytes<T>(this Stream stream, Span<T> span)
            where T : struct
        {
            stream.ReadExactly(MemoryMarshal.Cast<T, byte>(span));
        }

        public static void WriteBytes<T>(this Stream stream, in T value)
            where T : struct
        {
            stream.Write(MemoryMarshal.Cast<T, byte>(new ReadOnlySpan<T>(in value)));
        }

        public static void WriteBytes<T>(this Stream stream, ReadOnlySpan<T> span)
            where T : struct
        {
            stream.Write(MemoryMarshal.Cast<T, byte>(span));
        }

        public static void WriteBytes<T>(this Stream stream, Span<T> span)
            where T : struct
        {
            stream.Write(MemoryMarshal.Cast<T, byte>(span));
        }

        public static byte[] ReadAllToArrayAndDispose(this Stream stream)
        {
            using (stream)
            {
                if (stream is MemoryStream ms)
                    return ms.ToArray();
                else
                {
                    using (ms = new MemoryStream())
                    {
                        stream.CopyTo(ms);
                        return ms.ToArray();
                    }
                }
            }
        }

        public static Task CopyToAsync(this Stream source, Stream destination, long? knownTotalSize, StatusUpdater? onStatus, CancellationToken cancellationToken)
            => CopyToAsync(source, destination, knownTotalSize, onStatus, 81920, cancellationToken);

        public static async Task CopyToAsync(this Stream source, Stream destination, long? knownTotalSize, StatusUpdater? onStatus, int bufferSize, CancellationToken cancellationToken)
        {
            var total = source.CanSeek ? source.Length : (knownTotalSize ?? 0);
            var done = 0L;
            byte[] buffer = ArrayPool<byte>.Shared.Rent(bufferSize);
            try
            {
                int bytesRead;
                while ((bytesRead = await source.ReadAsync(new Memory<byte>(buffer), cancellationToken).ConfigureAwait(false)) != 0)
                {
                    await destination.WriteAsync(new ReadOnlyMemory<byte>(buffer, 0, bytesRead), cancellationToken).ConfigureAwait(false);
                    done += bytesRead;
                    if (total > 0 && onStatus != null)
                        await onStatus(done, total, null, null);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
            if (total == 0 && onStatus != null)
                await onStatus(done, done, null, null);
        }
    }
}
