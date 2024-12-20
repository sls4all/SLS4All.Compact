// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    /// <summary>
    /// Extends <see cref="BackgroundService"/> by running the <see cref="ExecuteAsync"/> on thread pool and providing basic logging.
    /// Without it if the <see cref="ExecuteAsync"/> does work without yielding, the application start will be blocked.
    /// </summary>
    public abstract class BackgroundThreadService : BackgroundService
    {
        private readonly ILogger _logger;
        private readonly TaskScheduler _scheduler;

        protected TaskScheduler Scheduler => _scheduler;

        public BackgroundThreadService(
            ILogger logger,
            TaskScheduler? scheduler = null)
        {
            _logger = logger;
            _scheduler = scheduler ?? TaskScheduler.Default;
        }

        protected sealed override Task ExecuteAsync(CancellationToken cancel)
            => Task.Factory.StartNew(async () =>
            {
                try
                {
                    await ExecuteTaskAsync(cancel);
                }
                catch (Exception ex)
                {
                    if (!cancel.IsCancellationRequested)
                        _logger.LogCritical(ex, $"Unhandled exception");
                    cancel.ThrowIfCancellationRequested();
                    throw;
                }
            }, cancel, TaskCreationOptions.None, _scheduler).Unwrap();

        protected abstract Task ExecuteTaskAsync(CancellationToken cancel);
    }
}
