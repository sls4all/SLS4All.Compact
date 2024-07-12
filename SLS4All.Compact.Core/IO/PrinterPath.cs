// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using Lexical.FileSystem.Internal;

namespace SLS4All.Compact.IO
{
    public readonly record struct PrinterPath(bool IsSystemPath, string PathUnsafe)
    {
        public const string Drives = "/drives";
        public const string Root = "/";
        public const string RootDrive = "/drives/root";
        public const string Home = "/home";
        public const string Downloads = "/downloads";
        public const string Jobs = "/jobs";
        public const string Objects = "/objects";
        public const string Surface = "/surface";
        public const string Backups = "/backups";
        public const string Documents = "/documents";
        public const string JobExtension = ".s4a";
        public const string BackupExtension = ".s4abak";
        public static readonly string[] PackageMasks = new[] { "*.s4a", "*.zip", "*.gz", "*.s4abak" };

        public string PathSafe
        {
            get
            {
                if (IsSystemPath)
                    return FromSystemPath(PathUnsafe).PathUnsafe;
                else
                    return PathUnsafe;
            }
        }

        public static string Combine(params PrinterPath[] parts)
        {
            var sb = new StringBuilder();
            foreach (var _part in parts)
            {
                var part = _part.PathSafe;
                if (part.StartsWith('/'))
                {
                    sb.Clear();
                    sb.Append(part);
                }
                else
                {
                    if (sb.Length > 0 && sb[^1] != '/')
                        sb.Append('/');
                    sb.Append(part);
                }
            }
            return sb.ToString();
        }

        private static bool TryGetRelative(string path, string root, string replacement, [MaybeNullWhen(false)] out string result)
        {
            if (path == root)
            {
                result = replacement;
                return true;
            }
            else if (path.StartsWith(CompactPathExtensions.UserProfileDirectory + "\\"))
            {
                result = Combine(replacement, path.Substring(root.Length + 1).Replace('\\', '/'));
                return true;
            }
            else
            {
                result = default!;
                return false;
            }
        }

        public static PrinterPath FromSystemPath(string path)
        {
            var res = System.IO.Path.GetFullPath(path);
            string? knownPath;
            if (TryGetRelative(res, CompactPathExtensions.UserProfileDirectory, Home, out knownPath) ||
                TryGetRelative(res, CompactPathExtensions.DownloadsDirectory, Downloads, out knownPath))
                return knownPath;
            if (OperatingSystem.IsWindows())
            {
                // windows drive path
                if (res.Length > 1 && res[1] == ':')
                {
                    res = res.Replace('\\', '/');
                    return Combine(Drives, res);
                }
                else
                    throw new ArgumentException("Invalid or unsupported path");
            }
            else
            {
                if (res.StartsWith('/'))
                    res = res.Substring(1);
                else
                    throw new ArgumentException("Invalid or unsupported path");
                return Combine(RootDrive, res);
            }
        }

        public static string? GetDirectoryName(PrinterPath path)
            => System.IO.Path.GetDirectoryName(path.PathSafe)?.Replace('\\', '/');

        public static string? GetLastDirectoryName(PrinterPath path)
        {
            var safe = path.PathSafe;
            if (safe.EndsWith('/'))
                return System.IO.Path.GetFileName(safe.Substring(0, safe.Length - 1));
            else
                return System.IO.Path.GetFileName(safe);
        }

        public static string GetFileNameToFirstDot(PrinterPath path)
        {
            var filename = System.IO.Path.GetFileName(path.PathSafe);
            var dot = filename.IndexOf('.');
            if (dot == -1)
                return filename;
            else
                return filename.Substring(0, dot);
        }

        public static string GetFileName(PrinterPath path)
            => System.IO.Path.GetFileName(path.PathSafe);

        public static string GetExtension(PrinterPath path)
            => System.IO.Path.GetExtension(path.PathSafe);

        public static string GetFileNameWithoutExtension(PrinterPath path)
            => System.IO.Path.GetFileNameWithoutExtension(path.PathSafe);

        public static string GetParent(PrinterPath path)
            => PathEnumerable.GetParent(path.PathSafe);

        public static implicit operator PrinterPath(string filename)
            => new PrinterPath(false, filename);
    }
}
