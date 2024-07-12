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
    public sealed class DelegatedComparer<T> : IComparer<T>
    {
        public Func<T?, T?, int> Comparer { get; }

        public DelegatedComparer(Func<T?, T?, int> comparer)
        {
            Comparer = comparer;
        }

        public int Compare(T? x, T? y)
            => Comparer(x, y);
    }

    public static class DelegatedComparer
    {
        public static DelegatedComparer<T> Create<T>(Func<T?, T?, int> comparer)
            => new DelegatedComparer<T>(comparer);
    }
}
