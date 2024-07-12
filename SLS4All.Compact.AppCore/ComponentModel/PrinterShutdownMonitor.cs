// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components.Forms;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.IO;
using SLS4All.Compact.Power;
using SLS4All.Compact.Storage.PrinterSettings;
using SLS4All.Compact.Temperature;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public class PrinterShutdownMonitorOptions
    {
        public bool TemperatureCheckEnabled { get; set; } = true;
        public double UnsafeTemperatureIncreaseAfterShutdown { get; set; } = 10;
        public double UnsafeChamberTemperatureAfterShutdown { get; set; } = 100;
        public double UnsafeSurfaceTemperatureAfterShutdown { get; set; } = 100;
        public TimeSpan ShutdownGracePeriod { get; set; } = TimeSpan.FromSeconds(5);
    }

    public sealed class PrinterShutdownMonitor : IPrinterClientInitializer
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<PrinterShutdownMonitorOptions> _options;
        private readonly IToastProvider _toastProvider;
        private readonly IPrinterLifetime _printerLifetime;
        private readonly ITemperatureClient _temperatureClient;
        private readonly IInputClient _inputClient;
        private readonly Dictionary<string, double> _shutdownTemperatures;
        private readonly object _syncRoot = new();
        private bool _temperaturesUnsafe;

        public PrinterShutdownMonitor(
            ILogger<PrinterShutdownMonitor> logger,
            IOptionsMonitor<PrinterShutdownMonitorOptions> options,
            IToastProvider toastProvider,
            ITemperatureClient temperatureClient,
            IPrinterLifetime printerLifetime,
            IInputClient inputClient)
        {
            _logger = logger;
            _options = options;
            _toastProvider = toastProvider;
            _temperatureClient = temperatureClient;
            _printerLifetime = printerLifetime;
            _inputClient = inputClient;
            _shutdownTemperatures = new();
        }

        public Task InitializeClient(IPrinterClient client, IPrinterClientCommandContext? context, CancellationToken cancel)
        {
            client.FirmwareShutdownEvent.AddHandler(OnShutdown);
            var reason = client.ShutdownReason;
            if (reason != null)
                return OnShutdown(reason, cancel).AsTask();
            else
                return Task.CompletedTask;
        }

        private ValueTask OnShutdown(string reason, CancellationToken token)
        {
            _toastProvider.Show(new ToastMessage
            {
                HeaderText = "Firmware shutdown",
                BodyText = $"Firmware has shutdown: {reason}. You may need to restart the software or reboot.",
                Type = ToastMessageType.Error,
            });
            _temperatureClient.StateChangedHighFrequency.AddHandler((state, cancel) =>
            {
                MonitorTemperatures(state);
                return ValueTask.CompletedTask;
            });
            MonitorTemperatures(null);
            return ValueTask.CompletedTask;
        }

        private async void MonitorTemperatures(TemperatureState? state)
        {
            try
            {
                var options = _options.CurrentValue;
                if (!options.TemperatureCheckEnabled)
                    return;
                lock (_syncRoot)
                {
                    if (_temperaturesUnsafe)
                        return;
                    if (state == null)
                        state = _temperatureClient.CurrentState;
                    var inputState = _inputClient.CurrentState;
                    var lidOpen = inputState.TryGetEntry(_inputClient.LidClosedId, out var entry) && entry.Value == false;
                    var surfaceIds = _temperatureClient
                        .SurfaceSensorIds
                        .Select(x => x.Id)
                        .ToHashSet();
                    var ids = _temperatureClient
                        .SurfaceSensorIds
                        .Concat(_temperatureClient.PowderChamberSensorIds)
                        .Concat(_temperatureClient.PrintChamberSensorIds)
                        .Select(x => x.Id)
                        .ToHashSet();
                    foreach (var item in state.Entries)
                    {
                        if (!ids.Contains(item.Id))
                            continue;
                        if (_shutdownTemperatures.TryGetValue(item.Id, out var existing))
                        {
                            if (surfaceIds.Contains(item.Id))
                            {
                                if (existing > options.UnsafeSurfaceTemperatureAfterShutdown &&
                                    item.AverageTemperature > existing + options.UnsafeTemperatureIncreaseAfterShutdown &&
                                    !lidOpen)
                                    _temperaturesUnsafe = true;
                            }
                            else
                            {
                                if (existing > options.UnsafeChamberTemperatureAfterShutdown &&
                                    item.AverageTemperature > existing + options.UnsafeTemperatureIncreaseAfterShutdown)
                                    _temperaturesUnsafe = true;
                            }
                        }
                        else
                            _shutdownTemperatures.Add(item.Id, item.AverageTemperature);
                    }
                    if (!_temperaturesUnsafe)
                        return;
                }

                var msg = "Printer software is shutdown and the printer temperature is still rising. Printer will now turn off to prevent any hazards!";
                _logger.LogCritical(msg);
                _toastProvider.Show(new ToastMessage
                {
                    HeaderText = "Temperature exceeding",
                    BodyText = msg,
                    Type = ToastMessageType.Error,
                });
                await Task.Delay(options.ShutdownGracePeriod);
                await _printerLifetime.PerformShutdown(new PrinterLifetimeRequest(PrinterShutdownMode.ShutdownSystem));
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Unhandled exception while monitoring temperatures");
            }
        }
    }
}
