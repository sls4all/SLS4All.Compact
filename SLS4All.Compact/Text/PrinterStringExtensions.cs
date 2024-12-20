// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SLS4All.Compact.Text
{
    public static class PrinterStringExtensions
    {
        private static ConcurrentDictionary<string, Regex> s_cleanupRegexes = new();

        public ref struct LineSplitEnumerator
        {
            private ReadOnlySpan<char> _str;

            public LineSplitEntry Current { get; private set; }

            public LineSplitEnumerator(ReadOnlySpan<char> str)
            {
                _str = str;
                Current = default;
            }

            public LineSplitEnumerator GetEnumerator() => this;

            public bool MoveNext()
            {
                var span = _str;
                if (span.Length == 0) // Reach the end of the string
                    return false;

                var index = span.IndexOfAny('\r', '\n');
                if (index == -1) // The string is composed of only one line
                {
                    _str = ReadOnlySpan<char>.Empty; // The remaining string is an empty string
                    Current = new LineSplitEntry(span, ReadOnlySpan<char>.Empty);
                    return true;
                }

                if (index < span.Length - 1 && span[index] == '\r')
                {
                    // Try to consume the '\n' associated to the '\r'
                    var next = span[index + 1];
                    if (next == '\n')
                    {
                        Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 2));
                        _str = span.Slice(index + 2);
                        return true;
                    }
                }

                Current = new LineSplitEntry(span.Slice(0, index), span.Slice(index, 1));
                _str = span.Slice(index + 1);
                return true;
            }
        }

        public readonly ref struct LineSplitEntry
        {
            public LineSplitEntry(ReadOnlySpan<char> line, ReadOnlySpan<char> separator)
            {
                Line = line;
                Separator = separator;
            }

            public ReadOnlySpan<char> Line { get; }
            public ReadOnlySpan<char> Separator { get; }

            // This method allow to deconstruct the type, so you can write any of the following code
            // foreach (var entry in str.SplitLines()) { _ = entry.Line; }
            // foreach (var (line, endOfLine) in str.SplitLines()) { _ = line; }
            // https://docs.microsoft.com/en-us/dotnet/csharp/deconstruct?WT.mc_id=DT-MVP-5003978#deconstructing-user-defined-types
            public void Deconstruct(out ReadOnlySpan<char> line, out ReadOnlySpan<char> separator)
            {
                line = Line;
                separator = Separator;
            }

            // This method allow to implicitly cast the type into a ReadOnlySpan<char>, so you can write the following code
            // foreach (ReadOnlySpan<char> entry in str.SplitLines())
            public static implicit operator ReadOnlySpan<char>(LineSplitEntry entry) => entry.Line;

            public override string ToString()
                => new string(Line);
        }

        public static LineSplitEnumerator SplitLines(this string str)
            => new LineSplitEnumerator(str.AsSpan());

        public static LineSplitEnumerator SplitLines(this ReadOnlySpan<char> str)
            => new LineSplitEnumerator(str);

        private static Regex GetQueryStringCleanupRegex(string key)
        {
            if (!s_cleanupRegexes.TryGetValue(key, out var res))
            {
                res = new Regex("(?:\\?|&)" + Regex.Escape(key) + "(?:=[^&]*&?|$)|(?:\\?|&)" + Regex.Escape(key) + "(?:=[^&]*|$)", RegexOptions.CultureInvariant | RegexOptions.Compiled);
                if (!s_cleanupRegexes.TryAdd(key, res))
                    res = s_cleanupRegexes[key];
            }
            return res;
        }

        public static string AppendQueryString(string url, string? key, object? value, bool doNotEncode = false, bool add = true)
        {
            if (add && key != null)
            {
                if (!doNotEncode)
                {
                    key = Uri.EscapeDataString(key);
                    if (value != null)
                    {
                        var valueRaw = Convert.ToString(value, CultureInfo.InvariantCulture);
                        value = valueRaw != null ? Uri.EscapeDataString(valueRaw) : null;
                    }
                }
                var cleanupRegex = GetQueryStringCleanupRegex(key);
                while (true)
                {
                    var urlNew = cleanupRegex.Replace(url, static match => match.Value[0] == '?' ? "?" : "", 1);
                    if (urlNew == url)
                        break;
                    url = urlNew;
                }
                if (value != null)
                {
                    if (!url.EndsWith('&') && !url.EndsWith('?'))
                    {
                        if (url.IndexOf('?') != -1)
                            url += "&";
                        else
                            url += "?";
                    }
                    url += key + "=" + value;
                }
                while (url.EndsWith('?'))
                    url = url.Substring(0, url.Length - 1);
                while (url.EndsWith('&'))
                    url = url.Substring(0, url.Length - 1);
            }
            return url;
        }
    }
}
