// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.ComponentModel
{
    [Flags]
    public enum SafeShutdownIssues
    {
        None = 0,
        UserMustIntervene = 1,
        PrintingInProgress = 2,
        TemperatureNotSafe = 4,
        HeatersEnabled = 8,
    }

    public readonly record struct SafeShutdownTemperature(bool IsSafe, double CurrentTemperature, double SafeTemperature);

    public interface ISafeShutdownManager
    {
        AsyncEvent ShutdownScheduledChangedEvent { get; }
        bool IsShutdownScheduled { get; }
        PrinterShutdownMode ScheduledShutdownMode { get; }

        Task<SafeShutdownTemperature> GetSafeTemperature(CancellationToken cancel = default);
        Task<SafeShutdownIssues> GetShutdownIssues(CancellationToken cancel = default);
        Task ScheduleShutdown(bool enabled, PrinterShutdownMode mode = PrinterShutdownMode.NotSet, CancellationToken cancel = default);
    }
}