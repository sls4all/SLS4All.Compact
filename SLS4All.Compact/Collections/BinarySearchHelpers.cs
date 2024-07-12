// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public static class BinarySearchHelpers
    {
        public static Span<T> BinarySearchEqualRange<T, TComparer>(this Span<T> source, T value, TComparer comparer)
            where TComparer : IComparer<T>
        {
            var index = source.BinarySearch(value, comparer);
            if (index < 0)
                return Span<T>.Empty;
            var start = index;
            while (start > 0)
            {
                if (comparer.Compare(source[start - 1], value) == 0)
                    start--;
                else
                    break;
            }
            var end = index;
            while (end + 1 < source.Length)
            {
                if (comparer.Compare(source[end + 1], value) == 0)
                    end++;
                else
                    break;
            }
            return source.Slice(start, end - start + 1);
        }

        public static void BinarySearchEqualRange<T, TComparer>(this Span<T> source, T value, TComparer comparer, out int first, out int count)
            where TComparer : IComparer<T>
        {
            var index = source.BinarySearch(value, comparer);
            if (index < 0)
            {
                first = ~index;
                count = 0;
                return;
            }
            var start = index;
            while (start > 0)
            {
                if (comparer.Compare(source[start - 1], value) == 0)
                    start--;
                else
                    break;
            }
            var end = index;
            while (end + 1 < source.Length)
            {
                if (comparer.Compare(source[end + 1], value) == 0)
                    end++;
                else
                    break;
            }
            first = start;
            count = end - start + 1;
        }
    }
}
