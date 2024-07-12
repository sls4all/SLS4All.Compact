// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Helpers;

namespace SLS4All.Compact.TestPlugin
{
    public class PluginServiceOptions
    {
        public string Message { get; set; } = "";
    }

    public class PluginService : IDelayedConstructable
    {
        public PluginService(
            ILogger<PluginService> logger, 
            IOptions<PluginServiceOptions> options)
        {
            logger.LogInformation($"Plugin loaded. Message: {options.Value.Message}");
        }
    }
}
