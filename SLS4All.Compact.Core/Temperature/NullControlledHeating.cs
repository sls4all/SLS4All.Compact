// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public sealed class NullControlledHeating : IControlledHeating
    {
        public bool IsRunning => false;

        public Task Cancel()
            => Task.CompletedTask;

        public Task CoolDown(ControlledCoolingSetup setup, StatusUpdater? onStatus = null, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task HeatUp(ControlledHeatingSetup setup, StatusUpdater? onStatus = null, Func<double?, CancellationToken, Task>? duringSurface = null, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task SurfaceHeatUp(ControlledHeatingSetup setup, StatusUpdater? onStatus = null, CancellationToken cancel = default)
            => Task.CompletedTask;

        public (TimeSpan time, double temperature) EstimateCoolDown(ControlledCoolingSetup setup, double? startingTemperature, TimeSpan elapsed = default)
            => (TimeSpan.Zero, 0);

        public (TimeSpan time, double temperature) EstimateHeatUp(ControlledHeatingSetup setup, double? startingTemperature, TimeSpan elapsed = default)
            => (TimeSpan.Zero, 0);

        public (TimeSpan time, double temperature) EstimateSurfaceHeatUp(ControlledHeatingSetup setup, double? startingTemperature, TimeSpan elapsed = default)
            => (TimeSpan.Zero, 0);

        public ControlledCoolingSetup CalcSetupRate(ControlledCoolingSetup setup, double? startingTemperature)
            => setup.Clone();

        public ControlledHeatingSetup FillWithCurrentTargets(ControlledHeatingSetup setup)
            => setup.Clone();
    }
}
