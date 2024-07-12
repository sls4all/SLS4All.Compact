// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public class GCServiceOptions
    {
        public TimeSpan CollectPeriodMin { get; set; } = TimeSpan.FromMinutes(1);
        public TimeSpan CollectPeriodMax { get; set; } = TimeSpan.FromSeconds(2);
    }

    public sealed class GCService : BackgroundThreadService
    {
        private readonly ILogger<GCService> _logger;
        private readonly IOptionsMonitor<GCServiceOptions> _options;

        public GCService(
            ILogger<GCService> logger,
            IOptionsMonitor<GCServiceOptions> options) : base(logger)
        {
            _logger = logger;
            _options = options;
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            while (true)
            {
                var options = _options.CurrentValue;
                var delay = TimeSpan.FromSeconds(options.CollectPeriodMin.TotalSeconds + Random.Shared.NextDouble() * (options.CollectPeriodMax.TotalSeconds - options.CollectPeriodMin.TotalSeconds));
                await Task.Delay(delay, cancel);
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, false);
            }
        }
    }
}
