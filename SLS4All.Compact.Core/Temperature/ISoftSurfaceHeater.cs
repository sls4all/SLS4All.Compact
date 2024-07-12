// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.IO;
using System;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public interface ISoftSurfaceHeater : ISurfaceHeater
    {
        bool IsRunning { get; }
        double? SoftSurfaceTargetTemperature { get; set; }
        Task LoadedTask { get; }
        string LastTargetInfo { get; }

        Task Start(PrinterPath? filename, byte[]? data, Func<byte[], CancellationToken, Task>? loaded, Action<Exception>? errorHandler);
        Task Stop();
    }
}