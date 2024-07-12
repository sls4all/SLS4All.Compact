// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Xml.Linq;

namespace SLS4All.Compact.Printer
{
    public interface IPrinterMaintenanceManager
    {
        public bool IsMaintenanceRequired =>
            HalogenRemaining <= TimeSpan.Zero ||
            LaserRemaining <= TimeSpan.Zero ||
            Z1Remaining <= 0 ||
            Z2Remaining <= 0 ||
            RRemaining <= 0;
        /// <summary>
        /// Active halogen time remaining for maintenance
        /// </summary>
        TimeSpan HalogenRemaining { get; }
        /// <summary>
        /// Active laser time remaining for maintenance
        /// </summary>
        TimeSpan LaserRemaining { get; }
        /// <summary>
        /// Z1 distance remaining for maintenance [m]
        /// </summary>
        double Z1Remaining { get; }
        /// <summary>
        /// Z2 distance remaining for maintenance [m]
        /// </summary>
        double Z2Remaining { get; }
        /// <summary>
        /// R distance remaining for maintenance [m]
        /// </summary>
        double RRemaining { get; }

        DateTimeOffset? LastHalogenReset { get; }
        DateTimeOffset? LastLaserReset { get; }
        DateTimeOffset? LastRReset { get; }
        DateTimeOffset? LastZ1Reset { get; }
        DateTimeOffset? LastZ2Reset { get; }

        ValueTask ResetHalogen(CancellationToken cancel = default);
        ValueTask ResetLaser(CancellationToken cancel = default);
        ValueTask ResetZ1(CancellationToken cancel = default);
        ValueTask ResetZ2(CancellationToken cancel = default);
        ValueTask ResetR(CancellationToken cancel = default);
    }
}