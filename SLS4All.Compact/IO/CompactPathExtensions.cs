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
    public static class CompactPathExtensions
    {
        private static char[] s_allPathSeparators = new char[] { '/', '\\' };
        private static readonly char[] UnsafeChars = "/\\<>:\"|?*".ToCharArray();
        private static readonly Regex[] UnsafeNames = (new[]
        {
            "CON", "PRN", "AUX", "NUL",
            "COM\\d+",
            "LPT\\d+",
        }).Select(x => new Regex($"(?<=^|/|\\\\){x}(?=/|\\\\|\\.|$)", RegexOptions.Compiled | RegexOptions.IgnoreCase)).ToArray();
        
        public static string UserProfileDirectory { get; }
        public static string DownloadsDirectory { get; }

        static CompactPathExtensions()
        {
            UserProfileDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (OperatingSystem.IsWindows())
                DownloadsDirectory = SHGetKnownFolderPath(new("374DE290-123F-4565-9164-39C4925E467B"), 0, 0);
            else
                DownloadsDirectory = Path.Combine(UserProfileDirectory, "Downloads");
        }

        public static string? GetFileNameOSUniversal(string? path)
        {
            if (path == null)
                return null;
            var index = path.LastIndexOfAny(s_allPathSeparators);
            if (index == -1)
                return path;
            else
                return path.Substring(index + 1);
        }

        public static string ResolvePath(string path)
        {
            if (path.StartsWith("~/") || path.StartsWith("~\\"))
                path = UserProfileDirectory + path[1..];
            return Path.GetFullPath(path);
        }

        public static void EnsureDirectoryExistsEmpty(string path)
        {
            [DoesNotReturn]
            static void Throw()
                => throw new InvalidOperationException("Cannot empty a root directory");

            // basic sanity check
            path = Path.GetFullPath(path);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                if (path.Length is >= 2 and <= 3 && path[1] == ':')
                    Throw();
            }
            else
            {
                if (path == "/")
                    Throw();
            }
            Directory.CreateDirectory(path);
            var dir = new DirectoryInfo(path);
            foreach (var entry in dir.GetFileSystemInfos())
                entry.Delete();
        }

        public static string EscapeName(string name)
            => Uri.EscapeDataString(name).Replace(".", "%2E").Replace("%20", " ");

        public static string GetSafeName(string name)
        {
            var sb = new StringBuilder(name);
            while (sb.Length > 0 && sb[^1] == '.')
                sb.Length--;
            foreach (var ch in UnsafeChars)
                sb.Replace(ch, '_');
            name = sb.ToString();
            foreach (var pattern in UnsafeNames)
                name = pattern.Replace(name, x => "_" + x.Value);
            return name;
        }

        [DllImport("shell32", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = false)]
        private static extern string SHGetKnownFolderPath(
                [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags,
                nint hToken = 0);
    }
}
