// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public delegate Task StatusUpdater(double done, double total, TimeSpan? estimate, object? status);
    
    public interface IStatusUpdater
    {
        Task UpdateProgress(double done, double total, TimeSpan? estimate, object? status);
    }

    public interface IStatusUpdater<TStatus> : IStatusUpdater
        where TStatus: class
    {
        Task UpdateProgress(double done, double total, TimeSpan? estimate, TStatus? status);
    }
}
