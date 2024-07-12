// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public sealed class NullHalogenClient : IHalogenClient, ILightsClient
    {
        public LightsState CurrentState => new LightsState(false);

        public int LightCount => 0;

        public AsyncEvent<LightsState> StateChangedLowFrequency { get; } = new();

        public AsyncEvent<LightsState> StateChangedHighFrequency { get; } = new();

        public ValueTask SetHalogens(bool enabled, int? mask = null, float? power = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask SetHalogens(Memory<(bool enabled, int index, float? power)> values, bool hidden, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public ValueTask SetLights(bool enabled, int? mask = null, float? power = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => ValueTask.CompletedTask;

        public IDisposable SupressValidation()
            => NullDisposable.Instance;

        public bool TryGetRecentLightPower(int index, out bool isCurrent, out float power, SystemTimestamp now, TimeSpan? duration, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
        {
            isCurrent = true;
            power = 0;
            return false;
        }

        public bool HasRecentLightPower(int index, SystemTimestamp now = default, TimeSpan? duration = null, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => false;

        public bool HasRecentLightPower(SystemTimestamp now = default, TimeSpan? duration = null, IPrinterClientCommandContext? context = null, CancellationToken cancel = default)
            => false;
    }
}
