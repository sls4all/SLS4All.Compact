// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileSystem;
using Lexical.FileSystem.Decoration;
using Microsoft.Extensions.FileProviders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public interface IPrinterFileProvider : IFileProvider
    {
        IPrinterFileSystem FileSystem { get; }

        void MoveFile(PrinterPath source, PrinterPath target);
        bool Exists(PrinterPath path);
        bool FileExists(PrinterPath path);
        bool DirectoryExists(PrinterPath path);
        string? Normalize(string? path);
        Stream OpenRead(PrinterPath filename);
        string GetHash(PrinterPath filename);
        Stream CreateFile(PrinterPath filename);
        void CreateDirectory(PrinterPath filename);
        void DeleteFile(PrinterPath filename);
        IEntry? TryGetEntry(PrinterPath filename);
        void CopyOverwrite(PrinterPath srcPath, PrinterPath dstPath);
        Task Eject(PrinterPath directory, CancellationToken cancel = default);
        bool IsEjectable(PrinterPath directory);
    }
}
