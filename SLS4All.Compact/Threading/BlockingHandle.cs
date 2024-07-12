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
    public sealed class BlockingHandle : IDisposable
    {
        private readonly ManualResetEventSlim _event;
        private Timer? _timer;

        public BlockingHandle(bool block)
        {
            _event = new ManualResetEventSlim(!block);
        }

        public void BlockFor(TimeSpan duration)
        {
            if (duration < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(duration));
            lock (_event)
            {
                if (duration == TimeSpan.Zero)
                {
                    _event.Set();
                    return;
                }
                if (_timer != null)
                    _timer.Dispose();
                Timer timer = default!;
                timer = new Timer(_ =>
                {
                    lock (_event)
                    {
                        if (_timer == timer)
                        {
                            _event.Set();
                            _timer.Dispose();
                            _timer = null;
                        }
                    }
                }, null, duration, Timeout.InfiniteTimeSpan);
                _timer = timer;
                _event.Reset();
            }
        }

        public void Block()
        {
            lock (_event)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
                _event.Set();
            }
        }

        public void Unblock()
        {
            lock (_event)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
                _event.Reset();
            }
        }

        public void Dispose()
        {
            lock (_event)
            {
                if (_timer != null)
                {
                    _timer.Dispose();
                    _timer = null;
                }
                _event.Dispose();
            }
        }

        public void Wait(CancellationToken cancel)
        {
            _event.Wait(cancel);
        }
    }
}
