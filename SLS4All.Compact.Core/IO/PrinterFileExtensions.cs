// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Printer;
using System;
using System.Collections.Generic;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public static class PrinterFileExtensions
    {
        public static unsafe Memory<T> GetValuesFromAccessor<T>(MemoryMappedViewAccessor view)
            where T : unmanaged
        {
            var bytes = new Span<byte>((void*)view.SafeMemoryMappedViewHandle.DangerousGetHandle(), (int)view.Capacity);
            var data = MemoryMarshal.Cast<byte, T>(bytes);
            return UnmanagedMemoryManager.CreateUnsafeFromUnmovableSpan(data);
        }

        public static IEnumerable<T> GetValuesFromStream<T>(Stream stream)
            where T : unmanaged
        {
            var buf = new byte[Marshal.SizeOf<Vector2>()];
            while (true)
            {
                if (stream.Position == stream.Length)
                    yield break;
                stream.ReadExactly(buf);
                yield return MemoryMarshal.Cast<byte, T>(buf)[0];
            }
        }

        public static string GetBlockDeviceForPartition(string path)
        {
            var result = path.TrimEnd('0', '1', '2', '3', '4', '5', '6', '7', '8', '9');
            if (result != path)
            {
                if (result.EndsWith('p'))
                    return result[..^1];
            }
            return path;
        }

        public static async Task<string?> TryGetPartitionForPath(string path, CancellationToken cancel)
        {
            string? result = null;
            using (var deviceTask = new ProcessOutputHelper(
                null,
                null, null,
                "/usr/bin/df", "--output=source " + Path.GetFullPath(path), null,
                TimeSpan.Zero,
                null,
                (stream, cancel) =>
                {
                    using (var reader = new StreamReader(stream))
                    {
                        while (true)
                        {
                            var line = reader.ReadLine();
                            if (line == null)
                                break;
                            result = line;
                        }
                    }
                    return Task.CompletedTask;
                }))
            {
                using (cancel.Register(deviceTask.Dispose))
                {
                    await deviceTask.RunTask;
                }
            }
            return result;
        }

        public static void CopyDirectory(string sourceDir, string destinationDir, bool recursive, bool overwrite, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var dir = new DirectoryInfo(sourceDir);
            if (!dir.Exists)
                throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");
            var dirs = dir.GetDirectories(); // Cache directories before we start copying
            Directory.CreateDirectory(destinationDir);
            foreach (var file in dir.GetFiles())
            {
                cancel.ThrowIfCancellationRequested();
                var targetFilePath = Path.Combine(destinationDir, file.Name);
                file.CopyTo(targetFilePath, overwrite);
            }
            if (recursive)
            {
                foreach (var subDir in dirs)
                {
                    string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                    CopyDirectory(subDir.FullName, newDestinationDir, true, overwrite, cancel);
                }
            }
        }
    }
}
