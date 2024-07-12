// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Helpers;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public sealed class NullTemperatureClient : ITemperatureClient
    {
        public static NullTemperatureClient Instance { get; } = new();

        public string SurfaceId => "surface";
        public string AvgSurfaceId => "surfaceAvg";
        public TemperatureClientSensorPair[] PrintBedHeaterIds => [];

        public TemperatureClientSensorPair[] SurfaceSensorIds => Array.Empty<TemperatureClientSensorPair>();

        public TemperatureClientSensorPair[] ExtraSurfaceSensorIds => Array.Empty<TemperatureClientSensorPair>();

        public TemperatureClientSensorPair[] PrintChamberSensorIds => Array.Empty<TemperatureClientSensorPair>();

        public TemperatureClientSensorPair[] PowderChamberSensorIds => Array.Empty<TemperatureClientSensorPair>();

        public TemperatureState CurrentState => new TemperatureState(Array.Empty<TemperatureEntry>(), null);

        public AsyncEvent<TemperatureState> StateChangedLowFrequency { get; } = new();

        public AsyncEvent<TemperatureState> StateChangedHighFrequency { get; } = new();


        public Task SetTarget(string id, double? value, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => Task.CompletedTask;

        public IDisposable SuppressValidation(string id)
            => NullDisposable.Instance;

        public Task<bool> TryIncreaseTarget(string id, double offset, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => Task.FromResult(true);
    }
}
