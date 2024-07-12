// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace SLS4All.Compact.Diagnostics
{
    public sealed class TelegramLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, TelegramLogger> _loggers = new();

        private readonly Func<string, LogLevel, bool>? _filter;
        private readonly TelegramLoggerSender _sender;

        public TelegramLoggerProvider(
            ITelegramBotClient botClient,
            ChatId chatId,
            TimeSpan sendDelay,
            Func<string, LogLevel, bool>? filter)
        {
            _filter = filter;
            _sender = new TelegramLoggerSender(botClient, chatId, sendDelay);
        }

        public void Dispose()
        {
            _sender.Dispose();
        }

        public ILogger CreateLogger(string categoryName)
            => _loggers.GetOrAdd(categoryName, CreateLoggerImplementation);

        private TelegramLogger CreateLoggerImplementation(string categoryName)
            => new TelegramLogger(categoryName, _sender, _filter);
    }
}
