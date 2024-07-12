// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Diagnostics.Contracts;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public class ControlledHeatingSetup
    {
        public double TargetPowder { get; set; }
        public double TargetPrint { get; set; }
        public double TargetPrintBed { get; set; }
        public double SurfaceThreshold { get; set; }
        public double SurfaceTarget { get; set; }
        public double SurfaceTarget2 { get; set; }
        public TimeSpan SurfaceTarget2Time { get; set; }
        public double? Step { get; set; }
        public double? Rate { get; set; }
        public double? SurfaceRate { get; set; }
        public double? Tolerance { get; set; }
        public TimeSpan? MinimumTime { get; set; }
        public double? LayersTemperatureStart { get; set; }
        public double? LayersTemperatureEnd { get; set; }
        public TimeSpan? LayersPeriod { get; set; }

        public ControlledHeatingSetup Clone()
            => (ControlledHeatingSetup)MemberwiseClone();
    }

    public class ControlledCoolingSetup
    {
        public double Target { get; set; }
        public double SurfaceThreshold1 { get; set; }
        public double SurfaceThreshold2 { get; set; }
        public double? Step { get; set; }
        public double? Rate1 { get; set; }
        public double? Rate2 { get; set; }
        public double? Rate3 { get; set; }
        public double? Tolerance { get; set; }
        public TimeSpan? MinimumTime { get; set; }

        public ControlledCoolingSetup Clone()
            => (ControlledCoolingSetup)MemberwiseClone();
    }

    public interface IControlledHeating
    {
        bool IsRunning { get; }
        Task HeatUp(
            ControlledHeatingSetup setup, 
            StatusUpdater? onStatus = null, 
            Func<double?, CancellationToken, Task>? duringSurface = null,
            CancellationToken cancel = default);
        Task CoolDown(ControlledCoolingSetup setup, StatusUpdater? onStatus = null, CancellationToken cancel = default);
        (TimeSpan time, double temperature) EstimateHeatUp(ControlledHeatingSetup setup, double? startingTemperature, TimeSpan elapsed = default);
        (TimeSpan time, double temperature) EstimateCoolDown(ControlledCoolingSetup setup, double? startingTemperature, TimeSpan elapsed = default);
        Task Cancel();
        ControlledCoolingSetup CalcSetupRate(ControlledCoolingSetup setup, double? startingTemperature);
        ControlledHeatingSetup FillWithCurrentTargets(ControlledHeatingSetup setup);

        (TimeSpan time, double temperature) EstimateSurfaceHeatUp(ControlledHeatingSetup setup, double? startingTemperature, TimeSpan elapsed = default);
        Task SurfaceHeatUp(
            ControlledHeatingSetup setup,
            StatusUpdater? onStatus = null,
            CancellationToken cancel = default);
    }
}