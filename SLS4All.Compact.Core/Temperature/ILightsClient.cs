// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Printer;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public interface ILightsClient
    {
        LightsState CurrentState { get; }
        int LightCount { get; }

        ValueTask SetLights(bool enabled, int? mask = null, float? power = null, bool hidden = false, IPrinterClientCommandContext? context = null, CancellationToken cancel = default);
    }
}