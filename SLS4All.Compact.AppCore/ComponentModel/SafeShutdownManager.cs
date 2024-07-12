// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Pages;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Printing;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.ComponentModel
{
    public class SafeShutdownManagerOptions
    {
        public double SafeTemperature { get; set; } = 90;
        public double SafeTemperatureHysteresis { get; set; } = 3;
        public TimeSpan CheckPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan GraceDelay { get; set; } = TimeSpan.FromSeconds(10);
    }

    public sealed class SafeShutdownManager : ISafeShutdownManager
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<SafeShutdownManagerOptions> _options;
        private readonly ITemperatureClient _temperature;
        private readonly ISurfaceHeater _surface;
        private readonly IPrintingService _printing;
        private readonly IPrinterLifetime _lifetime;
        private readonly object _lock = new();
        private CancellationTokenSource? _cancelSource;
        private Task _task = Task.CompletedTask;
        private PrinterShutdownMode _scheduledMode;
        private bool _temperatureSafe;

        public AsyncEvent ShutdownScheduledChangedEvent { get; } = new AsyncEvent();

        public bool IsShutdownScheduled
        {
            get
            {
                lock (_lock)
                {
                    return !_task.IsCompleted;
                }
            }
        }

        public PrinterShutdownMode ScheduledShutdownMode
        {
            get
            {
                lock (_lock)
                {
                    if (!_task.IsCompleted)
                        return _scheduledMode;
                    else
                        return PrinterShutdownMode.NotSet;
                }
            }
        }

        public SafeShutdownManager(
            ILogger<SafeShutdownManager> logger,
            IOptionsMonitor<SafeShutdownManagerOptions> options,
            ITemperatureClient temperature,
            ISurfaceHeater surface,
            IPrintingService printing,
            IPrinterLifetime lifetime)
        {
            _logger = logger;
            _options = options;
            _temperature = temperature;
            _surface = surface;
            _lifetime = lifetime;
            _printing = printing;
        }

        public async Task<SafeShutdownIssues> GetShutdownIssues(CancellationToken cancel)
        {
            var issues = SafeShutdownIssues.None;
            var temps = await GetSafeTemperature(cancel);
            if (_printing.IsPrinting)
                issues |= SafeShutdownIssues.PrintingInProgress | SafeShutdownIssues.UserMustIntervene;
            if (!temps.IsSafe)
                issues |= SafeShutdownIssues.TemperatureNotSafe;
            if (_temperature.CurrentState.Entries.Any(x => x.TargetTemperature != null) || _surface.TargetTemperature != null)
                issues |= SafeShutdownIssues.HeatersEnabled;
            return issues;
        }

        public async Task ScheduleShutdown(bool enabled, PrinterShutdownMode mode, CancellationToken cancel)
        {
            var task = Task.CompletedTask;
            lock (_lock)
            {
                if (enabled == IsShutdownScheduled)
                    return;
                if (!enabled)
                {
                    task = _task;
                    _cancelSource?.Cancel();
                    _cancelSource = null;
                    _task = Task.CompletedTask;
                }
                else if (_task.IsCompleted)
                {
                    _cancelSource = new();
                    _scheduledMode = mode == PrinterShutdownMode.NotSet ? PrinterShutdownMode.ShutdownSystem : mode;
                    _task = Task.Run(() => ScheduleTask(mode, _cancelSource.Token));
                }
            }
            await task;
            await ShutdownScheduledChangedEvent.Invoke(cancel);
        }

        private async Task ScheduleTask(PrinterShutdownMode mode, CancellationToken cancel)
        {
            try
            {
                var options = _options.CurrentValue;
                using var timer = new PeriodicTimer(options.CheckPeriod);
                while (await timer.WaitForNextTickAsync(cancel))
                {
                    var issues = await GetShutdownIssues(cancel);
                    if (issues == SafeShutdownIssues.None)
                    {
                        _logger.LogWarning("No issues for scheduled shutdown detected, waiting for GraceDelay");
                        await Task.Delay(options.GraceDelay, cancel);
                        issues = await GetShutdownIssues(cancel);
                        if (issues == SafeShutdownIssues.None)
                        {
                            _logger.LogWarning("No issues for scheduled shutdown detected even after GraceDelay, continuing with shutdown!");
                            await _lifetime.PerformShutdown(new PrinterLifetimeRequest(mode));
                            break;
                        }
                        else
                        {
                            _logger.LogInformation($"Issues arose after GraceDelay ({issues}), will continue to run and monitor");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    _logger.LogError(ex, "Unhandled exception in ScheduleTask");
            }
        }

        public Task<SafeShutdownTemperature> GetSafeTemperature(CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            lock (_lock)
            {
                var options = _options.CurrentValue;
                var state = _temperature.CurrentState;
                var ids = _temperature.PowderChamberSensorIds.Concat(_temperature.PrintChamberSensorIds).Concat(_temperature.SurfaceSensorIds).Select(x => x.Id).ToHashSet();
                var current = state.Entries.Where(x => ids.Contains(x.Id)).Select(x => x.CurrentTemperature).DefaultIfEmpty(0).Max();
                var safe = options.SafeTemperature;
                if (!_temperatureSafe && current <= safe)
                    _temperatureSafe = true;
                else if (_temperatureSafe && current > safe + options.SafeTemperatureHysteresis)
                    _temperatureSafe = false;
                return Task.FromResult(new SafeShutdownTemperature(_temperatureSafe, current, safe));
            }
        }
    }
}
