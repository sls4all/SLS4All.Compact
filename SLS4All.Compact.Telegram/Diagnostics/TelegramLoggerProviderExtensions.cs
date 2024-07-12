// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace SLS4All.Compact.Diagnostics
{
    public static class TelegramLoggerProviderExtensions
    {
        public static ILoggerFactory AddTelegramLogger(
            this ILoggerFactory loggerFactory,
            TelegramLoggerOptions options,
            Func<string, LogLevel, bool>? filter = default)
        {
            var botClient = new TelegramBotClient(options.BotToken);
            loggerFactory.AddProvider(new TelegramLoggerProvider(botClient, options.ChatId, options.SendDelay, filter));
            return loggerFactory;
        }

        public static ILoggerFactory AddTelegramLogger(
            this ILoggerFactory loggerFactory,
            Action<TelegramLoggerOptions> configure,
            Func<string, LogLevel, bool>? filter = default)
        {
            var options = new TelegramLoggerOptions();
            configure(options);
            return loggerFactory.AddTelegramLogger(options, filter);
        }

        public static ILoggerProvider? TryCreateTelegramProvider(
            IConfiguration configuration)
        {
            var telegram = configuration.GetSection("Telegram");
            if (!telegram.Exists())
                return null;
            var options = telegram.Get<TelegramLoggerOptions>();
            if (options == null || !options.Enabled || string.IsNullOrWhiteSpace(options.BotToken) || string.IsNullOrWhiteSpace(options.ChatId) || options.LogLevel == null)
                return null;
            if (!options.LogLevel.TryGetValue("Default", out var defaultLevel))
                defaultLevel = LogLevel.None;
            var botClient = new TelegramBotClient(options.BotToken);
            return new TelegramLoggerProvider(
                botClient,
                new ChatId(options.ChatId),
                options.SendDelay,
                (categoryName, level) =>
                {
                    if (options.LogLevel.TryGetValue(categoryName, out var categoryLevel))
                        return level >= categoryLevel;
                    else
                        return level >= defaultLevel;
                });
        }
    }
}
