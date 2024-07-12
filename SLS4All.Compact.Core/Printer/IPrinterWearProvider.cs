// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Xml.Linq;

namespace SLS4All.Compact.Printer
{
    public interface IPrinterWearProvider
    {
        DateTimeOffset FirstUpdateTime { get; }
        TimeSpan HalogenDuration { get; }
        TimeSpan LaserDuration { get; }
        /// <summary>
        /// Total recoater distance [m]
        /// </summary>
        double RDistance { get; }
        /// <summary>
        /// Total Z1 distance [m]
        /// </summary>
        double Z1Distance { get; }
        /// <summary>
        /// Total Z2 distance [m]
        /// </summary>
        double Z2Distance { get; }
    }
}