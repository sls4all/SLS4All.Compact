// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.McuClient.Pins
{
    public interface IMcuSpi
    {
        IMcu Mcu { get; }
        Task SetRate(int rate, CancellationToken cancel);
        Task Continuous(Func<CancellationToken, Task> func, CancellationToken cancel);
        void Send(ArraySegment<byte> data, int priority, McuOccasion clock);
        Task SendWait(ArraySegment<byte> data, int priority, McuOccasion clock, CancellationToken cancel);
        Task<ArraySegment<byte>> Transfer(ArraySegment<byte> data, int priority, McuOccasion clock, CancellationToken cancel = default);
    }
}