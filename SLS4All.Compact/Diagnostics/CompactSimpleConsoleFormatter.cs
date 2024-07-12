// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace SLS4All.Compact.Diagnostics
{
    /// <remarks>
    /// Modified <see cref="SimpleConsoleFormatter"/> from .NET sources
    /// </remarks>
    public sealed class CompactSimpleConsoleFormatter : ConsoleFormatter, IDisposable
    {
        public const string FormatterName = "SLS4AllSimple";

        private const string LoglevelPadding = ": ";
        private static readonly string _messagePadding = new string(' ', GetLogLevelString(LogLevel.Information).Length + LoglevelPadding.Length);
        private static readonly string _newLineWithMessagePadding = Environment.NewLine + _messagePadding;
        private IDisposable? _optionsReloadToken;

        public CompactSimpleConsoleFormatter(IOptionsMonitor<SimpleConsoleFormatterOptions> options)
            : base(FormatterName)
        {
            ReloadLoggerOptions(options.CurrentValue);
            _optionsReloadToken = options.OnChange(ReloadLoggerOptions);
        }

        [MemberNotNull(nameof(FormatterOptions))]
        private void ReloadLoggerOptions(SimpleConsoleFormatterOptions options)
        {
            FormatterOptions = options;
        }

        public void Dispose()
        {
            _optionsReloadToken?.Dispose();
        }

        internal SimpleConsoleFormatterOptions FormatterOptions { get; set; }

        public override void Write<TState>(in LogEntry<TState> logEntry, IExternalScopeProvider? scopeProvider, TextWriter textWriter)
        {
            string message = logEntry.Formatter(logEntry.State, logEntry.Exception);
            if (logEntry.Exception == null && message == null)
            {
                return;
            }
            LogLevel logLevel = logEntry.LogLevel;
            ConsoleColors logLevelColors = GetLogLevelConsoleColors(logLevel);
            string logLevelString = GetLogLevelString(logLevel);

            string? timestamp = null;
            string? timestampFormat = FormatterOptions.TimestampFormat;
            if (timestampFormat != null)
            {
                DateTimeOffset dateTimeOffset = GetCurrentDateTime();
                timestamp = dateTimeOffset.ToString(timestampFormat);
            }
            if (timestamp != null)
            {
                textWriter.Write(timestamp);
            }
            if (logLevelString != null)
            {
                textWriter.WriteColoredMessage(logLevelString, logLevelColors.Background, logLevelColors.Foreground);
            }
            CreateDefaultLogMessage(textWriter, logEntry, message, scopeProvider);
        }

        private void CreateDefaultLogMessage<TState>(TextWriter textWriter, in LogEntry<TState> logEntry, string message, IExternalScopeProvider? scopeProvider)
        {
            bool singleLine = FormatterOptions.SingleLine;
            int eventId = logEntry.EventId.Id;
            Exception? exception = logEntry.Exception;

            // Example:
            // info: ConsoleApp.Program[10] [Program.cs@100] @0.000
            //       Request received

            // category and event id
            using (textWriter.CreateColorScope(ConsoleColor.Black, ConsoleColor.Cyan))
            {
                textWriter.Write(LoglevelPadding);
                textWriter.Write(logEntry.Category);
                textWriter.Write('[');

                Span<char> span = stackalloc char[10];
                if (eventId.TryFormat(span, out int charsWritten))
                    textWriter.Write(span.Slice(0, charsWritten));
                else
                    textWriter.Write(eventId.ToString());

                textWriter.Write(']');
            }

            if (logEntry.State is IReadOnlyList<KeyValuePair<string, object>> args &&
                IsStructuredLoggerMessage(message, args, out var callerFilePath, out var callerLineNumber, out var trimmedMessage))
            {
                textWriter.Write(" [");
                textWriter.Write(callerFilePath);
                textWriter.Write('@');
                textWriter.Write(callerLineNumber);
                textWriter.Write(']');
                message = trimmedMessage;
            }

            using (textWriter.CreateColorScope(ConsoleColor.Black, ConsoleColor.DarkRed))
            {
                var now = DateTime.Now;
                textWriter.Write(' ');
                textWriter.Write(now.ToString("HH:mm:ss.fff"));
            }

            if (!singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }

            // scope information
            WriteScopeInformation(textWriter, scopeProvider, singleLine);
            WriteMessage(textWriter, message, singleLine);

            // Example:
            // System.InvalidOperationException
            //    at Namespace.Class.Function() in File:line X
            if (exception != null)
            {
                // exception message
                WriteMessage(textWriter, exception.ToString(), singleLine);
            }
            if (singleLine)
            {
                textWriter.Write(Environment.NewLine);
            }
        }

        /// <summary>
        /// Find [filename@line] and the end of the message
        /// </summary>
        private bool IsStructuredLoggerMessage(
            string message,
            IReadOnlyList<KeyValuePair<string, object>> args,
            [MaybeNullWhen(false)] out string callerFilePath,
            [MaybeNullWhen(false)] out string callerLineNumber,
            [MaybeNullWhen(false)] out string trimmedMessage)
        {
            trimmedMessage = null;
            callerFilePath = null;
            callerLineNumber = null;
            if (!message.EndsWith(']'))
                return false;
            var startBrace = message.LastIndexOf('[');
            if (startBrace == -1)
                return false;
            var at = message.IndexOf('@', startBrace + 1);
            if (at == -1)
                return false;
            trimmedMessage = message.Substring(0, startBrace);
            for (int i = 0, c = args.Count; i < c; i++)
            {
                var pair = args[i];
                if (pair.Key == StructuredLoggerExtensions.CallerFilePathKey)
                    callerFilePath = pair.Value.ToString();
                else if (pair.Key == StructuredLoggerExtensions.CallerLineNumberKey)
                    callerLineNumber = pair.Value.ToString();
            }
            return callerFilePath != null && callerLineNumber != null;
        }

        private static void WriteMessage(TextWriter textWriter, string message, bool singleLine)
        {
            if (!string.IsNullOrEmpty(message))
            {
                if (singleLine)
                {
                    textWriter.Write(' ');
                    WriteReplacing(textWriter, Environment.NewLine, " ", message);
                }
                else
                {
                    textWriter.Write(_messagePadding);
                    WriteReplacing(textWriter, Environment.NewLine, _newLineWithMessagePadding, message);
                    textWriter.Write(Environment.NewLine);
                }
            }

            static void WriteReplacing(TextWriter writer, string oldValue, string newValue, string message)
            {
                string newMessage = message.Replace(oldValue, newValue);
                writer.Write(newMessage);
            }
        }

        private DateTimeOffset GetCurrentDateTime()
        {
            return FormatterOptions.UseUtcTimestamp ? DateTimeOffset.UtcNow : DateTimeOffset.Now;
        }

        private static string GetLogLevelString(LogLevel logLevel)
        {
            return logLevel switch
            {
                LogLevel.Trace => "trce",
                LogLevel.Debug => "dbug",
                LogLevel.Information => "info",
                LogLevel.Warning => "warn",
                LogLevel.Error => "fail",
                LogLevel.Critical => "crit",
                _ => throw new ArgumentOutOfRangeException(nameof(logLevel))
            };
        }

        private ConsoleColors GetLogLevelConsoleColors(LogLevel logLevel)
        {
            bool disableColors = (FormatterOptions.ColorBehavior == LoggerColorBehavior.Disabled) ||
                (FormatterOptions.ColorBehavior == LoggerColorBehavior.Default && System.Console.IsOutputRedirected);
            if (disableColors)
            {
                return new ConsoleColors(null, null);
            }
            // We must explicitly set the background color if we are setting the foreground color,
            // since just setting one can look bad on the users console.
            return logLevel switch
            {
                LogLevel.Trace => new ConsoleColors(ConsoleColor.Gray, ConsoleColor.Black),
                LogLevel.Debug => new ConsoleColors(ConsoleColor.Gray, ConsoleColor.Black),
                LogLevel.Information => new ConsoleColors(ConsoleColor.DarkGreen, ConsoleColor.Black),
                LogLevel.Warning => new ConsoleColors(ConsoleColor.Yellow, ConsoleColor.Black),
                LogLevel.Error => new ConsoleColors(ConsoleColor.Black, ConsoleColor.DarkRed),
                LogLevel.Critical => new ConsoleColors(ConsoleColor.White, ConsoleColor.DarkRed),
                _ => new ConsoleColors(null, null)
            };
        }

        private void WriteScopeInformation(TextWriter textWriter, IExternalScopeProvider? scopeProvider, bool singleLine)
        {
            if (FormatterOptions.IncludeScopes && scopeProvider != null)
            {
                bool paddingNeeded = !singleLine;
                scopeProvider.ForEachScope((scope, state) =>
                {
                    if (paddingNeeded)
                    {
                        paddingNeeded = false;
                        state.Write(_messagePadding);
                        state.Write("=> ");
                    }
                    else
                    {
                        state.Write(" => ");
                    }
                    state.Write(scope);
                }, textWriter);

                if (!paddingNeeded && !singleLine)
                {
                    textWriter.Write(Environment.NewLine);
                }
            }
        }

        private readonly struct ConsoleColors
        {
            public ConsoleColors(ConsoleColor? foreground, ConsoleColor? background)
            {
                Foreground = foreground;
                Background = background;
            }

            public ConsoleColor? Foreground { get; }

            public ConsoleColor? Background { get; }
        }
    }
}
