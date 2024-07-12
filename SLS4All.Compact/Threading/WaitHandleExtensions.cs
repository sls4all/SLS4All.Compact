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

namespace SLS4All.Compact.Threading
{
    public static class WaitHandleExtensions
    {
        public static ValueTask<bool> WaitOneAsync(this WaitHandle waitHandle, TimeSpan timeout)
        {
            if (waitHandle.WaitOne(0))
                return ValueTask.FromResult(true);
            else
                return new ValueTask<bool>(WaitOneAsyncInner(waitHandle, timeout));
        }

        private static Task<bool> WaitOneAsyncInner(this WaitHandle waitHandle, TimeSpan timeout)
        {
            var tcs = new TaskCompletionSource<bool>();
            var rwh = ThreadPool.RegisterWaitForSingleObject(
                waitHandle, 
                static (state, timedOut) => ((TaskCompletionSource<bool>)state!).TrySetResult(!timedOut), 
                tcs, 
                timeout, 
                true);
            var task = tcs.Task;
            task.ContinueWith(static (prev, state) => ((RegisteredWaitHandle)state!).Unregister(null), rwh);
            return task;
        }
    }
}
