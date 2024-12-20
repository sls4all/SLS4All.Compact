// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public sealed class NullSurfaceHeater : ISoftSurfaceHeater
    {
        public static NullSurfaceHeater Instance { get; } = new();

        public bool IsRunning => false;

        public double? SoftSurfaceTargetTemperature { 
            get => null; 
            set { } 
        }

        public Task LoadedTask => Task.CompletedTask;

        public double? TargetTemperature => null;

        public bool TargetReached => true;

        public string LastTargetInfo => "";

        public Task<IAsyncDisposable> ForceConstantLights(CancellationToken cancel)
            => Task.FromResult<IAsyncDisposable>(NullDisposable.Instance);

        public double? GetInternalBaseTemperature()
            => null;

        public Task SetTarget(double? value, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task Start(PrinterPath? filename, byte[]? data, Func<byte[], CancellationToken, Task>? loaded, Action<Exception>? errorHandler)
            => Task.CompletedTask;

        public Task Stop(CancellationToken cancel)
            => Task.CompletedTask;

        public Task<bool> TryIncreaseTarget(double offset, CancellationToken cancel)
            => Task.FromResult(true);
    }
}
