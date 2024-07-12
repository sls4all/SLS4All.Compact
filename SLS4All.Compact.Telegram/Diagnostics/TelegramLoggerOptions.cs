// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace SLS4All.Compact.Diagnostics
{
    public class TelegramLoggerOptions
    {
        public bool Enabled { get; set; } = true;
        public string ChatId { get; set; } = default!;
        public string BotToken { get; set; } = default!;
        public Dictionary<string, LogLevel> LogLevel { get; set; } = new();
        public TimeSpan SendDelay { get; set; } = TimeSpan.FromSeconds(1);
    }
}
