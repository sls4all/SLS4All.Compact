// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public class FakePrinterClientOptions : PrinterClientBaseOptions
    {
    }

    public sealed class FakePrinterClient : PrinterClientBase, IPrinterClient
    {
        private readonly ILogger _logger;
        private readonly IEnumerable<IObjectFactory<IPrinterClientInitializer, object>> _initalizers;

        public override bool IsConnected => true;
        public override long ConnectionIndex => 0;
        public override bool ShouldSendSafeCheckpoints => false;
        public override bool IsShutdown => false;
        public override string? ShutdownReason => null;
        public override bool HasLostCommunication => false;

        public FakePrinterClient(
            ILogger<FakePrinterClient> logger,
            IOptionsMonitor<FakePrinterClientOptions> options,
            IObjectFactory<IMovementClient, object> movementFactory,
            IEnumerable<IObjectFactory<IPrinterClientInitializer, object>> initalizers)
            : base(logger, options, movementFactory)
        {
            _logger = logger;
            _initalizers = initalizers;

            Task.Run(() => Initialize());
        }

        private async Task Initialize()
        {
            foreach (var initializer in _initalizers)
            {
                try
                {
                    await initializer.CreateAndCall(x => x.InitializePrinter(default));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception while initializing printer with {initializer}");
                }
            }
            foreach (var initializer in _initalizers)
            {
                try
                {
                    await initializer.CreateAndCall(x => x.InitializeClient(this, null, default));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Exception while initializing client with {initializer}");
                }
            }
        }

        public override Task Restart(PrinterClientRestartFlags type, CancellationToken cancel)
            => Task.CompletedTask;

        public override Task WaitForConnection(CancellationToken cancel)
            => Task.CompletedTask;

        public override void Shutdown(string reason, Exception? ex, IPrinterClientCommandContext? context)
        {
        }

        public override (string Key, string Message)[] GetConnectionStatus()
            => [];
    }
}
