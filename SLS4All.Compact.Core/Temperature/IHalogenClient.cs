// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public interface IHalogenClient
    {
        LightsState CurrentState { get; }
        int LightCount { get; }
        AsyncEvent<LightsState> StateChangedLowFrequency { get; }
        AsyncEvent<LightsState> StateChangedHighFrequency { get; }

        IDisposable SupressValidation();
        ValueTask SetHalogens(bool enabled, int? mask = null, float? power = null, bool hidden = false, bool forceMax = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        ValueTask SetHalogens(Memory<(bool enabled, int index, float? power)> values, bool hidden, bool forceMax = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        bool TryGetRecentLightPower(int index, out bool isCurrent, out float power, SystemTimestamp now, TimeSpan? duration, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        bool HasRecentLightPower(int index, SystemTimestamp now = default, TimeSpan? duration = null, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
        bool HasRecentLightPower(SystemTimestamp now = default, TimeSpan? duration = null, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);

        float GetMaxHalogenFactor(IPrinterClientCommandContext? context = null);
    }
}