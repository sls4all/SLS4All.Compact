// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using Nito.AsyncEx;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
#pragma warning disable BL0006 // Do not use RenderTree types

namespace SLS4All.Compact.ComponentModel
{
    public class FirmwareConnectedNotifierOptions
    {
        public bool PlayMelodyWhenConnected { get; set; } = false;
    }

    public sealed class FirmwareConnectedNotifier : IDelayedConstructable, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<FirmwareConnectedNotifierOptions> _options;
        private readonly IPrinterClient _printer;
        private readonly IObjectFactory<IMelodyClient, object> _melodyClient;
        private readonly Lock _syncRoot = new();
        private readonly CancellationTokenSource _cancelSource;
        private bool _wasConnected;

        public FirmwareConnectedNotifier(
            ILogger<FirmwareConnectedNotifier> logger,
            IOptionsMonitor<FirmwareConnectedNotifierOptions> options,
            IPrinterClient printer,
            IObjectFactory<IMelodyClient, object> melodyClient)
        {
            _logger = logger;
            _options = options;
            _printer = printer;
            _melodyClient = melodyClient;
            _cancelSource = new CancellationTokenSource();

            _printer.ConnectedEvent.AddHandler(OnConnected);
            _ = OnConnected(default);
        }

        private ValueTask OnConnected(CancellationToken _)
        {
            var options = _options.CurrentValue;
            var cancel = _cancelSource.Token;
            lock (_syncRoot)
            {
                var isConnected = _printer.IsConnected;
                if (isConnected && !_wasConnected && options.PlayMelodyWhenConnected)
                {
                    _wasConnected = true;
                    Task.Run(async () =>
                    {
                        try
                        {
                            using (var melodyClient = _melodyClient.CreateDisposable())
                                await melodyClient.Instance.Play(Melody.Information, cancel);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to play melody");
                        }
                    });
                }
            }
            return ValueTask.CompletedTask;
        }

        public void Dispose()
        {
            _cancelSource.Dispose();
        }
    }
}
