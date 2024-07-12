// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public static class OptionsExtensions
    {
        public static TValue[] GetOrderedEnabledValues<TValue>(
            this Dictionary<string, TValue?> dictionary,
            Func<TValue, bool> getIsEnabled)
        {
            var res = new List<TValue>(dictionary.Count);
            foreach (var pair in dictionary
                .OrderByNatural(x => x.Key)
                .Where(x => x.Value != null && getIsEnabled(x.Value)))
            {
                if (pair.Key.EndsWith("!"))
                    res.Clear();
                res.Add(pair.Value!);
            }
            return res.ToArray();
        }

        public static KeyValuePair<string, TValue>[] GetOrderedEnabledKeyValues<TValue>(
            this Dictionary<string, TValue?> dictionary,
            Func<TValue, bool> getIsEnabled)
        {
            var res = new List<KeyValuePair<string, TValue>>(dictionary.Count);
            foreach (var pair in dictionary
                .OrderByNatural(x => x.Key)
                .Where(x => x.Value != null && getIsEnabled(x.Value)))
            {
                if (pair.Key.EndsWith("-"))
                    res.Clear();
                res.Add(pair!);
            }
            return res.ToArray();
        }

        public static KeyValuePair<string, TValue>[] GetOrderedEnabledKeyValues<TValue>(
            this Dictionary<string, TValue?> dictionary)
            where TValue : IOptionsItemEnable
            => GetOrderedEnabledKeyValues(dictionary, x => x.IsEnabled);

        public static TValue[] GetOrderedEnabledValues<TValue>(
            this Dictionary<string, TValue?> dictionary)
            where TValue : IOptionsItemEnable
            => GetOrderedEnabledValues(dictionary, x => x.IsEnabled);

        public static string[] GetOrderedEnabledValues(
            this Dictionary<string, string?> dictionary)
            => GetOrderedEnabledValues(dictionary, x => !string.IsNullOrEmpty(x));

        public static KeyValuePair<string, string?>[] GetOrderedEnabledKeyValues(
            this Dictionary<string, string?> dictionary)
            => GetOrderedEnabledKeyValues(dictionary, x => !string.IsNullOrEmpty(x))!;

        public static string?[][] GetOrderedEnabledValues(
            this Dictionary<string, string?[]?> dictionary)
            => GetOrderedEnabledValues(dictionary, x => (x?.Length > 0) == true);

        public static KeyValuePair<string, string?[]>[] GetOrderedEnabledKeyValues(
            this Dictionary<string, string?[]?> dictionary)
            => GetOrderedEnabledKeyValues(dictionary, x => (x?.Length > 0) == true);
    }
}
