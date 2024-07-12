// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.FileProviders;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace SLS4All.Compact.IO
{
    public static class PrinterPathExtensions
    {
        public static string GetPath(this IFileInfo fileInfo)
        {
            if (fileInfo is Lexical.FileSystem.Decoration.FileSystemProvider.FileInfo fsInfo)
                return fsInfo.Entry.Path;
            throw new InvalidOperationException("Entry has no path");
        }
    }
}
