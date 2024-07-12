// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using Telegram.Bot;

namespace SLS4All.Compact.Diagnostics
{
    public sealed class TelegramLogger : ILogger
    {
        private readonly string _category;
        private readonly Func<string, LogLevel, bool> _filter;
        private readonly TelegramLoggerSender _sender;

        internal TelegramLogger(
            string category,
            TelegramLoggerSender sender,
            Func<string, LogLevel, bool>? filter)
        {
            _category = category ?? throw new ArgumentNullException(nameof(category));
            _sender = sender;
            _filter = filter ?? (static (cat, logLevel) => true);
        }

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel))
                return;

            if (formatter == null)
                throw new ArgumentNullException(nameof(formatter));

            var message = formatter(state, exception);

            if (!string.IsNullOrWhiteSpace(message))
                SendMessage(logLevel, _category, eventId.Id, message, exception);
        }

        private void SendMessage(LogLevel logLevel, string logName, int eventId, string message, Exception? exception)
        {
            const int maxMessageSize = 4096 - 24;

            var logBuilder = new StringBuilder();
            var logLevelString = GetLogLevelString(logLevel);
            logBuilder.Append("<b>");
            logBuilder.Append("<u>");
            if (!string.IsNullOrEmpty(logLevelString))
                logBuilder.Append($"{HttpUtility.HtmlEncode(logLevelString)}: ");
            logBuilder.Append("</u>");

            logBuilder.Append(HttpUtility.HtmlEncode(logName));
            logBuilder.Append('[');
            logBuilder.Append(HttpUtility.HtmlEncode(eventId));
            logBuilder.Append(']');
            logBuilder.Append("</b>");
            logBuilder.AppendLine();

            logBuilder.Append("<pre>");
            if (!string.IsNullOrEmpty(message))
            {
                var truncated = false;
                string text;
                while (true)
                {
                    text = HttpUtility.HtmlEncode(message + (truncated ? "..." : ""));
                    if (logBuilder.Length + text.Length <= maxMessageSize)
                        break;
                    message = message.Substring(0, message.Length / 2);
                    truncated = true;
                }
                logBuilder.AppendLine(text);
            }

            if (exception != null)
            {
                logBuilder.Append("<i>");
                var truncated = false;
                var exceptionText = exception.ToString();
                string text;
                while (true)
                {
                    text = HttpUtility.HtmlEncode(exceptionText + (truncated ? "..." : ""));
                    if (logBuilder.Length + text.Length <= maxMessageSize)
                        break;
                    exceptionText = exceptionText.Substring(0, exceptionText.Length / 2);
                    truncated = true;
                }
                logBuilder.Append(text);
                logBuilder.Append("</i>");
                logBuilder.AppendLine();
            }
            logBuilder.Append("</pre>");

            var content = logBuilder.ToString();
            _sender.EnqueueMessage(content);
        }

        private static string? GetLogLevelString(LogLevel logLevel)
            => logLevel switch
            {
                LogLevel.Trace => "trce",
                LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                LogLevel.Critical => "crit",
                _ => null,
            };

        public bool IsEnabled(LogLevel logLevel)
            => _filter(_category, logLevel);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;
    }
}
