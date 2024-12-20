// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Temperature
{
    public interface ISurfaceHeater
    {
        double? TargetTemperature { get; }
        bool TargetReached { get; }

        double? GetInternalBaseTemperature();
        Task SetTarget(double? value, CancellationToken cancel = default);
        Task<bool> TryIncreaseTarget(double offset, CancellationToken cancel);
        Task<IAsyncDisposable> ForceConstantLights(CancellationToken cancel);
    }
}
