// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public sealed class NullPrinterSettings : IPrinterSettings
    {
        public static NullPrinterSettings Instance { get; } = new NullPrinterSettings();

        public AsyncEvent SettingsChanged { get; } = new();

        public PrinterPowerSettingsSnapshot Power { get; }
        public PrinterTemperatureSettingsSnapshot Temperature { get; }
        public PrinterWatchDogSettingsSnapshot WatchDog { get; }
        public PrinterLocalizationSettingsSnapshot Localization { get; }

        public NullPrinterSettings()
        {
            Localization = new()
            {
                PreferredTemperatureUnits = PrinterTemperatureUnits.Celsius,
                PreferredUnits = PrinterUnits.Metric,
            };
            Power = new()
            {
                HalogenMaxPercent = 100,
                LaserWattage = 10,
                MaxWattage = 1000000,
            };
            Temperature = new()
            {
                SurfaceOffset1 = 0,
                SurfaceOffset2 = 0,
                SurfaceTemperature1 = 0,
                SurfaceTemperature2 = 0,
            };
            WatchDog = new()
            {
                IsWatchDogEnabled = false,
            };
        }
    }
}
