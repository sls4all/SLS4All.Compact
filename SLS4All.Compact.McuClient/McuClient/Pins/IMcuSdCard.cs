// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Collections.Frozen;

namespace SLS4All.Compact.McuClient.Pins
{
    public interface IMcuSdCard
    {
        IMcu Mcu { get; }
        int SectorSize { get; }
        long TotalSectors { get; }
        bool IsWriteProtected { get; }
        FrozenDictionary<string, object> CardInfo { get; }

        Task ReadSectors(uint startSector, int count, Stream stream, CancellationToken cancel = default);
        Task WriteSectors(uint startSector, int count, Stream stream, CancellationToken cancel = default);
    }
}