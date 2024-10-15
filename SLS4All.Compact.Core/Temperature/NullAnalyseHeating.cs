// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public sealed class NullAnalyseHeating : IAnalyseHeating
    {
        public Task AnalyseTask => Task.CompletedTask;

        public bool IsRunning => false;

        public PrinterPath? LastFilename => null;

        public TimeSpan TotalEstimate => TimeSpan.Zero;

        public string PrivateDirectoryName => "private";

        public string DefaultExtension => ".tmp";

        public string[] FilenameMasks => [];

        public Task Start(string? name, AnalyseHeatingSetup setup, Action<Exception>? errorHandler, Action? completedHandler, StatusUpdater? onStatus = null, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task Stop()
            => Task.CompletedTask;
    }
}
