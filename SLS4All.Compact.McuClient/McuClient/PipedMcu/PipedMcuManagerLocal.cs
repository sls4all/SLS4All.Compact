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
using SLS4All.Compact.Storage.PrinterSettings;
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
    public sealed class PipedMcuManagerLocal : McuManager
    {
        public override IMcuPowerManager PowerManager => throw new NotSupportedException();
        public override FrozenDictionary<string, IMcuStepper> Steppers => throw new NotSupportedException();
        public override FrozenDictionary<string, IMcuOutputPin> OutputPins => throw new NotSupportedException();
        public override FrozenDictionary<string, IMcuButton> Buttons => throw new NotSupportedException();
        public override FrozenDictionary<string, IMcuTemperatureSensor> TemperatureSensors => throw new NotSupportedException();
        public override FrozenDictionary<string, IMcuHeater> Heaters => throw new NotSupportedException();
        public override FrozenDictionary<string, IMcuSdCard> SdCards => throw new NotSupportedException();
        public override TimeSpan StepperQueueHigh => throw new NotSupportedException();
        public override TimeSpan StepperQueueLow => throw new NotSupportedException();

        protected override bool HasKeepAliveEnablePinsEnabled => false;

        public PipedMcuManagerLocal(
            ILoggerFactory loggerFactory,
            IOptionsMonitor<McuManagerOptions> options,
            IAppDataWriter appDataWriter,
            IEnumerable<IMcuDeviceFactory> deviceFactories,
            IPrinterSettingsStorage settingsStorage)
            : base(loggerFactory, loggerFactory.CreateLogger<PipedMcuManagerLocal>(), options, appDataWriter, deviceFactories, settingsStorage)
        {
        }

        protected override IMcuClockSync CreateClockSync(ILoggerFactory loggerFactory)
            => new PipedMcuClockSyncLocal(loggerFactory.CreateLogger<PipedMcuClockSyncLocal>(), this);

        protected override IMcu CreateMcu(ILoggerFactory loggerFactory, IAppDataWriter appDataWriter, McuManager mcuManagerBase, IOptions<McuManagerOptions.ManagerMcuOptions> options, IMcuClockSync clockSync, IEnumerable<IMcuDeviceFactory> deviceFactories)
            => new PipedMcuLocal(loggerFactory, appDataWriter, mcuManagerBase, options, clockSync, deviceFactories, _settingsStorage);

        public override McuPinDescription ClaimPin(McuPinType type, string description, bool canInvert = false, bool canPullup = false, string? shareType = null)
            => throw new NotSupportedException();

        public override McuBusDescription ClaimBus(string description, string? shareType = null)
            => throw new NotSupportedException();
    }
}
