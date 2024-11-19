// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    /// <summary>
    /// Fully fledged <see cref="McuManager"/> that instead of the local <see cref="Mcu"/> creates <see cref="PipedMcuClockSyncProxy"/>
    /// </summary>
    public class PipedMcuManagerProxy : McuManagerLocal
    {
        public PipedMcuManagerProxy(
            ILoggerFactory loggerFactory, 
            IOptionsMonitor<McuManagerOptions> options, 
            IAppDataWriter appDataWriter,
            IPrinterSettings settingsStorage,
            IOptionsMonitor<McuStepperGlobalOptions>? optionsStepperGlobal = null, 
            ITemperatureCamera? temperatureCamera = null) : base(loggerFactory, options, appDataWriter, [], settingsStorage, optionsStepperGlobal, temperatureCamera)
        {
        }

        protected override IMcuClockSync CreateClockSync(ILoggerFactory loggerFactory)
            => new PipedMcuClockSyncProxy(loggerFactory.CreateLogger<PipedMcuClockSyncProxy>());

        protected override IMcu CreateMcu(ILoggerFactory loggerFactory, IAppDataWriter appDataWriter, McuManager mcuManagerBase, IOptions<McuManagerOptions.ManagerMcuOptions> options, IMcuClockSync clockSync, IEnumerable<IMcuDeviceFactory> deviceFactories)
            => new PipedMcuProxy(loggerFactory, appDataWriter, this, options, clockSync);

        public override bool TryCollectGarbageBlocking(bool performMajorCleanup)
        {
            if (!base.TryCollectGarbageBlocking(performMajorCleanup))
                return false;
            foreach (var mcu in _mcuItems.Values)
            {
                if (!mcu.Mcu.IsShutdown)
                {
                    try
                    {
                        if (((PipedMcuProxy)mcu.Mcu).TryCollectGarbageBlocking(performMajorCleanup))
                        {
                            // single MCU suffices, all MCUs lead to the same process
                            return true;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to collect garbage using MCU {mcu.Mcu}");
                    }
                }
            }
            return false;
        }
    }
}
