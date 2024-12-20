// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.McuClient.Sensors;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static SLS4All.Compact.McuClient.PipedMcu.PipedMcuCodec;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    /// <summary>
    /// Simplest <see cref="IMcuManager"/> that just creates local <see cref="Mcu"/> and provides them to be accessed trough <see cref="PipedMcuComponent"/>
    /// </summary>
    public class McuManagerFake : McuManagerLocal
    {
        public McuManagerFake(
            ILoggerFactory loggerFactory, 
            IOptionsMonitor<McuManagerOptions> options, 
            IAppDataWriter appDataWriter, 
            IEnumerable<IMcuDeviceFactory> deviceFactories, 
            IPrinterSettings settingsStorage, 
            IThreadStackTraceDumper stackTraceDumper, 
            IOptionsMonitor<McuStepperGlobalOptions>? optionsStepperGlobal = null, 
            ITemperatureCamera? temperatureCamera = null) 
            : base(loggerFactory, options, appDataWriter, deviceFactories, settingsStorage, stackTraceDumper, optionsStepperGlobal, temperatureCamera)
        {
        }

        protected override IMcu CreateMcu(ILoggerFactory loggerFactory, IAppDataWriter appDataWriter, McuManager mcuManagerBase, IOptions<McuManagerOptions.ManagerMcuOptions> options, IMcuClockSync clockSync, IEnumerable<IMcuDeviceFactory> deviceFactories)
            => new FakeMcu(
                loggerFactory,
                loggerFactory.CreateLogger<FakeMcu>(),
                appDataWriter,
                this,
                options,
                clockSync,
                deviceFactories.OfType<FakeMcuDeviceFactory>().Single());                
    }
}
