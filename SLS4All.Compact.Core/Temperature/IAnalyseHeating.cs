// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public interface IAnalyseHeating
    {
        Task AnalyseTask { get; }
        bool IsRunning { get; }
        TimeSpan TotalEstimate { get; }
        string DefaultExtension { get; }
        string[] FilenameMasks { get; }

        Task Start(
            string? name,
            Action<Exception>? errorHandler, 
            Action? completedHandler, 
            StatusUpdater? onStatus = null, 
            CancellationToken cancel = default);
        Task Stop();
    }
}