// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.UpdateModel
{
    public sealed record class ApplicationUpdateReport(
        DateTime CheckTime,
        ServerManifest Manifest,
        ApplicationInfo[] ApplicableInfos,
        ApplicationInfo[] NewerInfos,
        bool HadForbidden);

    public enum ApplicationUpdatePhase
    {
        NotSet = 0,
        CheckingForUpdates,
        DownloadingUpdate,
    }

    public sealed record class ApplicationUpdateStatus(
        ApplicationUpdatePhase Phase);

    public interface IApplicationUpdate
    {
        ApplicationInfo? CurrentInfo { get; }
        ApplicationUpdateReport? LastReport { get; }
        AsyncEvent StateChanged { get; }
        BackgroundTask<ApplicationUpdateStatus> BackgroundTask { get; }
        ApplicationInfo? PreparedUpdate { get; }

        Task CheckForUpdatesBackground(CancellationToken cancel = default);
        Task PrepareUpdate(ApplicationInfo info, CancellationToken cancel = default);
        Task ApplyUpdate(CancellationToken cancel = default);
    }
}