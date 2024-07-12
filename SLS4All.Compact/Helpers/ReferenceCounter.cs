// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public sealed class ReferenceCounter
    {
        private sealed class DisposableHelper : IDisposable
        {
            private ReferenceCounter? _owner;

            public DisposableHelper(ReferenceCounter owner)
            {
                Interlocked.Increment(ref owner._count);
            }

            public void Dispose()
            {
                var owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null)
                {
                    var res = Interlocked.Decrement(ref owner._count);
                    Debug.Assert(res > 0);
                }    
            }
        }

        private volatile int _count;

        public bool IsIncremented => _count > 0;

        public IDisposable Increment()
            => new DisposableHelper(this);
    }
}
