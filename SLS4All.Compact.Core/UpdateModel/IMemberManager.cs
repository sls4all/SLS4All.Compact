// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Metadata;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.UpdateModel
{
    public interface IMemberManager
    {
        AsyncEvent StateChanged { get; }
        bool HasMemberId { get; }
        bool ValidateMemberId(string id);
        Task<bool> TrySetMemberId(string? id, CancellationToken cancel = default);
        ValueTask<string?> GetCurrentMemberId(CancellationToken cancel = default);
    }
}
