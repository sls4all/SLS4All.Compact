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
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace SLS4All.Compact.Printer
{
    public class McuHostRunnerOptions : LoggedScriptRunnerOptions
    {
        public bool IsEnabled { get; set; } = false;
    }

    /// <summary>
    /// Placeholder class for logger
    /// </summary>
    public class McuRunnerOutput
    {
    }

    public class McuHostRunner : LoggedScriptRunner<McuRunnerOutput>, IPrinterFirmwareRunner
    {
        private readonly IOptionsMonitor<McuHostRunnerOptions> _options;
        private readonly IAppDataWriter _appDataWriter;

        public McuHostRunner(
            ILogger<McuHostRunner> logger,
            ILogger<McuRunnerOutput> outputLogger,
            IOptionsMonitor<McuHostRunnerOptions> options,
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
                        throw new InvalidOperationException($"Missing Klipper script/binary: {filename} ({Path.GetFullPath(filename)})");
                }
                var args = options.ArgsPlatform;
                return Run(filename, args, cancel);
            }
            else
                return cancel.WaitHandle.WaitOneAsync(Timeout.InfiniteTimeSpan).AsTask();
        }
    }
}
