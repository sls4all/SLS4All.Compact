// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public interface IPrinterFileEntry
    {
        bool IsDirectory { get; }

        string Path { get; }

        string Name { get; }

        DateTimeOffset LastModified { get; }

        DateTimeOffset LastAccess { get; }
    }

    public interface IPrinterFileProvider : IFileProvider
    {
        void Move(PrinterPath source, PrinterPath target, bool overwrite);
        bool Exists(PrinterPath path);
        bool FileExists(PrinterPath path);
        bool DirectoryExists(PrinterPath path);
        string? Normalize(string? path);
        Stream OpenRead(PrinterPath filename);
        void Delete(PrinterPath filename);
        string GetHash(PrinterPath filename);
        Stream CreateFile(PrinterPath filename);
        void CreateDirectory(PrinterPath filename);
        IPrinterFileEntry? TryGetEntry(PrinterPath filename);
        void CopyOverwrite(PrinterPath srcPath, PrinterPath dstPath);
        Task Eject(PrinterPath directory, CancellationToken cancel = default);
        bool IsEjectable(PrinterPath directory);
        IEnumerable<IPrinterFileEntry> Browse(PrinterPath directory);
        Stream Open(PrinterPath path, FileMode mode, FileAccess access, FileShare share);
    }
}
