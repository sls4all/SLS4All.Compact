// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public sealed class PeriodicForceTimer : IDisposable
    {
        private readonly PeriodicTimer _timer;
        private readonly AsyncAutoResetEvent _force;
        private CancellationToken _cancel;
        private Task? _tickTask;
        private Task? _forceTask;

        public PeriodicForceTimer(TimeSpan period)
        {
            _timer = new PeriodicTimer(period);
            _force = new AsyncAutoResetEvent(false);
        }

        public async Task WaitForNextTickAsync(CancellationToken cancel)
        {
            if (_cancel != cancel)
            {
                if (_cancel != default)
                    throw new InvalidOperationException("PeriodicForceTimer does not work with different cancellation tokens");
                _cancel = cancel;
            }
            cancel.ThrowIfCancellationRequested();
            if (_tickTask == null)
                _tickTask = _timer.WaitForNextTickAsync(cancel).AsTask();
            if (_forceTask == null)
                _forceTask = _force.WaitAsync(cancel);
            var task = await Task.WhenAny(_tickTask, _forceTask);
            if (task == _tickTask)
                _tickTask = null;
            else if (task == _forceTask)
                _forceTask = null;
            await task;
            cancel.ThrowIfCancellationRequested();
        }

        public void Force()
        {
            _force.Set();
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
