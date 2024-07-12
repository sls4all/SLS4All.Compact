// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    /// <summary>
    /// Represents a wildcard running on the
    /// <see cref="System.Text.RegularExpressions"/> engine.
    /// </summary>
    /// <remarks>
    /// Modified from https://www.codeproject.com/Articles/11556/Converting-Wildcards-to-Regexes
    /// </remarks>
    public class Wildcard : Regex
    {
        public string Pattern { get; }

        /// <summary>
        /// Initializes a wildcard with the given search pattern.
        /// </summary>
        /// <param name="pattern">The wildcard pattern to match.</param>
        public Wildcard(string pattern, bool compiled) : base(
            WildcardToRegex(pattern),
            RegexOptions.IgnoreCase | (compiled ? RegexOptions.Compiled : RegexOptions.None))
        {
            Pattern = pattern;
        }

        /// <summary>
        /// Initializes a wildcard with the given search pattern and options.
        /// </summary>
        /// <param name="pattern">The wildcard pattern to match.</param>
        /// <param name="options">A combination of one or more
        /// <see cref="System.Text.RegexOptions"/>.</param>
        public Wildcard(string pattern, RegexOptions options)
            : base(WildcardToRegex(pattern), options)
        {
            Pattern = pattern;
        }

        /// <summary>
        /// Converts a wildcard to a regex.
        /// </summary>
        /// <param name="pattern">The wildcard pattern to convert.</param>
        /// <returns>A regex equivalent of the given wildcard.</returns>
        public static string WildcardToRegex(string pattern)
        {
            return "(?:^|\\|/)" + Regex.Escape(pattern).
                 Replace("\\*", ".*").
                 Replace("\\?", ".") + "$";
        }

        public override bool Equals(object? obj)
            => obj is Wildcard other && other.Pattern == Pattern;

        public override int GetHashCode()
            => Pattern.GetHashCode();

        public static bool HasWildcards(string name)
            => name.Contains('?') || name.Contains('*');

        public static IEnumerable<string> FindFiles(string wildcardPath)
        {
            var parts = wildcardPath
                .Split(Path.DirectorySeparatorChar);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var driveCandidate = parts[0];
                if (driveCandidate.Length == 2 && driveCandidate[1] == ':')
                    return FindFilesInner(driveCandidate, parts.Skip(1).ToArray());
                else
                    return FindFilesInner(".", parts);
            }
            else
            {
                if (wildcardPath.StartsWith('/'))
                    return FindFilesInner("/", parts);
                else
                    return FindFilesInner(".", parts);
            }
        }

        private static IEnumerable<string> FindFilesInner(string path, string[] parts)
        {
            var name = parts[0];
            var isLast = parts.Length == 1;
            if (HasWildcards(name))
            {
                if (!isLast)
                {
                    var rest = parts.Skip(1).ToArray();
                    foreach (var subdir in Directory.GetDirectories(path, name))
                    {
                        foreach (var res in FindFilesInner(subdir, rest))
                            yield return res;
                    }
                }
                else
                {
                    foreach (var file in Directory.GetFiles(path, name))
                        yield return file;
                }
            }
            else
            {
                var sub = Path.Combine(path, name);
                if (!isLast)
                {
                    var rest = parts.Skip(1).ToArray();
                    foreach (var res in FindFilesInner(sub, rest))
                        yield return res;
                }
                else
                {
                    yield return sub;
                }
            }
        }
    }
}
