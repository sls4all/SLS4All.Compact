// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Diagnostics;

namespace SLS4All.Compact.ComponentModel
{
    public class FirstRenderExecutorOptions
    {
        public string Filename { get; set; } = "";
        public string Arguments { get; set; } = "";
    }

    public sealed class FirstRenderExecutor : IFirstRenderHandler
    {
        private readonly ILogger<FirstRenderExecutor> _logger;
        private readonly IOptionsMonitor<FirstRenderExecutorOptions> _options;
        private volatile bool _hasHandled;

        public bool HasHandled => _hasHandled;

        public FirstRenderExecutor(
            ILogger<FirstRenderExecutor> logger,
            IOptionsMonitor<FirstRenderExecutorOptions> options)
        {
            _logger = logger;
            _options = options;
        }

        public ValueTask HandleFirstRender()
        {
            if (!_hasHandled)
            {
                _hasHandled = true;
                var options = _options.CurrentValue;
                if (!string.IsNullOrWhiteSpace(options.Filename))
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            UseShellExecute = true,
                            FileName = options.Filename,
                            Arguments = options.Arguments,
                        });
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to execute configured process on first render");
                    }
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
