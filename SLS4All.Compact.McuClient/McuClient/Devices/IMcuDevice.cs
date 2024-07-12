// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Devices
{
    public record class McuDeviceInfo(string Alias, string Name, string Endpoint, int Baud);

    public interface IMcuDevice : IDisposable
    {
        IMcuDeviceFactory Factory { get; }
        McuDeviceInfo Info { get; }
        ValueTask<int> ReadBlock(McuCodec codec, CancellationToken cancel = default);
        ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancel = default);
        ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default);
        Task Flush(CancellationToken cancel = default);
    }
}
