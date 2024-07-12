// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Printer;

namespace SLS4All.Compact.McuClient
{
    public sealed class McuProxyComponent
    {
        private readonly ILogger _logger;
        private readonly IOptions<ApplicationOptions> _options;
        private readonly McuProxy _proxy;
        private readonly McuHostRunner _runner;

        public McuProxyComponent(
            ILogger<McuProxyComponent> logger,
            IOptions<ApplicationOptions> options,
            McuProxy proxy,
            McuHostRunner runner)
        {
            _logger = logger;
            _options = options;
            _proxy = proxy;
            _runner = runner;
        }

        public async Task Run()
        {
            var options = _options.Value;
            var runnerTask = Task.Run(async () =>
            {
                while (true)
                {
                    await _runner.Run(default);
                    await Task.Delay(1000);
                }
            });
            var proxyTask = _proxy.Run(options.Port);
            await await Task.WhenAny(runnerTask, proxyTask);
        }
    }
}
