// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public static class PrinterCollectionExtensions
    {
        private static readonly JsonSerializerOptions s_jsonEqualsOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
        };
        private static readonly Regex s_numberRegex = new Regex(@"\d+", RegexOptions.Compiled);

        private static (int Min, int Max) GetNumberSequenceLength(string text)
        {
            var min = int.MaxValue;
            var max = 0;
            var count = 0;
            foreach (var ch in text)
            {
                if (char.IsNumber(ch))
                    count++;
                else
                {
                    if (count < min && count != 0)
                        min = count;
                    if (count > max)
                        max = count;
                    count = 0;
                }
            }
            if (count < min && count != 0)
                min = count;
            if (count > max)
                max = count;
            return (min, max);
        }

        public static IOrderedEnumerable<T> OrderByNatural<T>(this IEnumerable<T> source, Func<T, string> selector)
        {
            var min = int.MaxValue;
            var max = 0;
            var list = source.Select(value =>
            {
                var key = selector(value);
                var length = GetNumberSequenceLength(key);
                if (length.Min < min)
                    min = length.Min;
                if (length.Max > max)
                    max = length.Max;
                return value;
            }).ToList();
            if (min == int.MaxValue || min == max)
                return list.OrderBy(selector);
            else
                return list
                    .OrderBy(x => s_numberRegex.Replace(selector(x), m => m.Value.PadLeft(max, '0')),
                        StringComparer.CurrentCultureIgnoreCase);
        }

        public static IEnumerable<T> AsEnumerableWithoutLength<T>(this IEnumerable<T> source)
        {
            foreach (var item in source)
                yield return item;
        }

        public static void Shuffle<T>(this IList<T> list, Random random)
        {
            Shuffle(list, random, 0, list.Count);
        }

        public static void Shuffle<T>(this IList<T> list, Random random, int index, int count)
        {
            for (int n = index + count - 1; n > index; --n)
            {
                int k = random.Next(n + 1);
                T temp = list[n];
                list[n] = list[k];
                list[k] = temp;
            }
        }

        public static void Shuffle<T>(this Span<T> span, Random random)
        {
            for (int n = span.Length - 1; n > 0; --n)
            {
                int k = random.Next(n + 1);
                T temp = span[n];
                span[n] = span[k];
                span[k] = temp;
            }
        }

        public static T CastByExistingValue<T>(object? value, T existing)
            => value != null ? (T)value : existing;

        public static bool JsonEquals<T>(T x, T y)
        {
            if (object.ReferenceEquals(x, y))
                return true;
            var strx = JsonSerializer.Serialize(x, s_jsonEqualsOptions);
            var stry = JsonSerializer.Serialize(y, s_jsonEqualsOptions);
            return strx == stry;
        }

        public static int GetJsonHashCode<T>(T obj)
        {
            var str = JsonSerializer.Serialize(obj, s_jsonEqualsOptions);
            return str.GetHashCode();
        }
    }
}
