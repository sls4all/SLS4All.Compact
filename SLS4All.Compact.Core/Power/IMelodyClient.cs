// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Power
{
    public enum Melody
    {
        NotSet = 0,
        Error,
        Warning,
        Information,
    }

    public interface IMelodyClient
    {
        Task Play(Melody melody, CancellationToken cancel = default);
    }
}