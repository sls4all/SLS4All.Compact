// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public sealed class NullLayerClient : ILayerClient
    {
        public static NullLayerClient Instance { get; } = new();

        public double MinimumPowderBedDepth => 5;

        public Task BedPreparation(BedPreparationSetup setup, StatusUpdater? onStatus, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task BeginLayer(BeginLayerSetup setup, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task BeginPrint(CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task EjectCake(double? expectedDepth, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task EndLayer(CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task EndPrint(CancellationToken cancel = default)
            => Task.CompletedTask;

        public PowderVolumeTotals GetPowderVolume(PowderVolumeSetup setup)
            => new PowderVolumeTotals(default, default, default, default, default, null, default, default);

        public Task HomeBedsAndRecoater(double powderChamberDepth, StatusUpdater? status = null, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task PrintCap(PrintCapSetup setup, StatusUpdater? onStatus, CancellationToken cancel = default)
            => Task.CompletedTask;

        public Task SetPowderDepth(double totalPowderChamberDepth, CancellationToken cancel = default)
            => Task.CompletedTask;
    }
}
