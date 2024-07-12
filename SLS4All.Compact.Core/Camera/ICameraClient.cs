// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public enum CameraMode
    {
        NotSet = 0,
        LaserMinimal,
        Printing,
        Sintering,
    }

    public interface ICameraClient
    {
        bool IsMostlyEmpty { get; }
        AsyncEvent<MimeData> ImageCaptured { get; }

        Task<IAsyncDisposable> SetCameraMode(CameraMode mode, CancellationToken cancel = default);
    }
}