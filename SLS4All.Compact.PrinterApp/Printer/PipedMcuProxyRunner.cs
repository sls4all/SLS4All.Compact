// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public class PipedMcuProxyRunnerOptions : LoggedScriptRunnerOptions
    {
        public bool IsEnabled { get; set; } = false;
    }

    /// <summary>
    /// Placeholder class for logger
    /// </summary>
    public class PipedMcuProxyRunnerOutput
    {
    }

    public class PipedMcuProxyRunner : LoggedScriptRunner<PipedMcuProxyRunnerOutput>, IPrinterFirmwareRunner
    {
        private readonly IOptionsMonitor<PipedMcuProxyRunnerOptions> _options;
        private readonly IAppDataWriter _appDataWriter;

        public PipedMcuProxyRunner(
            ILogger<McuHostRunner> logger,
            ILogger<PipedMcuProxyRunnerOutput> outputLogger,
            IOptionsMonitor<PipedMcuProxyRunnerOptions> options,
            IAppDataWriter appDataWriter)
            : base(logger, outputLogger, options)
        {
            _options = options;
            _appDataWriter = appDataWriter;
        }

        public Task UpdateConfiguration()
            => Task.CompletedTask;

        public Task Run(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;
            if (options.IsEnabled)
            {
                var optionsDir = _appDataWriter.GetPublicOptionsDirectory();
                var filename = Path.Combine(optionsDir, options.ExecutablePlatform);
                if (!Path.Exists(filename))
                {
                    filename = Path.Combine(AppContext.BaseDirectory, options.ExecutablePlatform);
                    if (!Path.Exists(filename))
                        throw new InvalidOperationException($"Missing McuProxy script/binary: {filename} ({Path.GetFullPath(filename)})");
                }
                var args = new StringBuilder(options.ArgsPlatform);
                var configurationSources = StartupBase.ConfigurationSources;
                for (int i = 0; i < configurationSources.Length; i++)
                {
                    args.Append($" --Application:{nameof(McuAppApplicationOptions.ConfigurationSources)}:{i}=\"{configurationSources[i]}\"");
                }
                return Run(filename, args.ToString(), cancel);
            }
            else
                return cancel.WaitHandle.WaitOneAsync(Timeout.InfiniteTimeSpan).AsTask();
        }
    }
}
