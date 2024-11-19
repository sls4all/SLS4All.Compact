// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;
using SLS4All.Compact.Numerics;

namespace SLS4All.Compact
{
    public record class PrinterPowerSettingsSnapshot
    {
        public decimal MaxWattage { get; init; }
        public decimal LaserWattage { get; init; }
        public decimal HalogenMaxPercent { get; init; }
    }

    public record class PrinterTemperatureSettingsSnapshot
    {
        public decimal SurfaceOffset1 { get; init; }
        public decimal SurfaceTemperature1 { get; init; }
        public decimal SurfaceOffset2 { get; init; }
        public decimal SurfaceTemperature2 { get; init; }
        public int ThermoCameraOffsetX { get; init; }
        public int ThermoCameraOffsetY { get; init; }
    }

    public record class PrinterWatchDogSettingsSnapshot
    {
        public bool IsWatchDogEnabled { get; init; }
    }

    public enum PrinterUnits
    {
        NotSet = 0,
        Metric = 1,
        Imperial = 2,
    }

    public enum PrinterTemperatureUnits
    {
        NotSet = 0,
        Celsius = 1,
        Fahrenheit = 2,
    }

    public class PrinterLocalizationSettingsSnapshot
    {
        public PrinterUnits PreferredUnits { get; init; }
        public PrinterTemperatureUnits PreferredTemperatureUnits { get; init; }
        public UnitConverterFlags UnitConverterFlags { get; init; }
    }

    public interface IPrinterSettings
    {
        AsyncEvent SettingsChanged { get; }

        PrinterPowerSettingsSnapshot Power { get; }
        PrinterTemperatureSettingsSnapshot Temperature { get; }
        PrinterWatchDogSettingsSnapshot WatchDog { get; }
        PrinterLocalizationSettingsSnapshot Localization { get; }
    }
}