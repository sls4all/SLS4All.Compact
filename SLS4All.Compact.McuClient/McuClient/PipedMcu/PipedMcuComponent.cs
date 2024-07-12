// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using static SLS4All.Compact.McuClient.PipedMcu.PipedMcuCodec;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    public sealed class PipedMcuComponent : BackgroundThreadService
    {
        private readonly ILogger<PipedMcuComponent> _logger;
        private readonly McuManager _manager;

        public PipedMcuComponent(
            ILogger<PipedMcuComponent> logger,
            McuManager manager)
            : base(logger)
        {
            _logger = logger;
            _manager = manager;
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            try
            {
                var runTask = _manager.Run(cancel, cancel);
                _ = _manager.HasStartedTask.ContinueWith(_ =>
                {
                    _logger.LogInformation($"Running MCU manager assigned");
                });
                await runTask;
                throw new McuException("Manager had ended");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, $"Unhandled exception while running manager, cancelling");
                throw;
            }
        }
    }
}
