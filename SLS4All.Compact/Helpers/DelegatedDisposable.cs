// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Helpers
{
    public sealed class DelegatedDisposable : IDisposable
    {
        private Action? _action;

        public DelegatedDisposable(Action action)
        {
            _action = action;
        }

        public void Dispose()
        {
            var action = Interlocked.Exchange(ref _action, null);
            if (action != null)
                action();
        }
    }
}
