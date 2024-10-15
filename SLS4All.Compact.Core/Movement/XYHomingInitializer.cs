// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Printer;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public class XYHomingInitializer : IPrinterClientInitializer
    {
        private readonly ILogger<XYHomingInitializer> _logger;
        private readonly IMovementClient _movementClient;

        public XYHomingInitializer(
            ILogger<XYHomingInitializer> logger,
            IMovementClient movementClient)
        {
            _logger = logger;
            _movementClient = movementClient;
        }

        public virtual async Task InitializeClient(IPrinterClient client, IPrinterClientCommandContext? context, CancellationToken cancel)
        {
            _logger.LogDebug($"Homing XY");
            await _movementClient.HomeXY(context, cancel);
        }
    }
}
