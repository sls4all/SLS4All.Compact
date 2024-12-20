// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Diagnostics
{
    public class SimpleFileLogger : ILogger, IDisposable
    {
        private readonly StreamWriter _writer;
        private const string LoglevelPadding = ": ";
        private static readonly string _messagePadding = new string(' ', GetLogLevelString(LogLevel.Information).Length + LoglevelPadding.Length);
        private static readonly string _newLineWithMessagePadding = Environment.NewLine + _messagePadding;

        public LogLevel MinLogLevel { get; set; } = LogLevel.Trace;
        public bool LogCaller { get; set; } = true;
        public bool LogEventId { get; set; } = true;

        public SimpleFileLogger(string filename, bool append)
        {
            _writer = new StreamWriter(filename, append);
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
            => null;

        public bool IsEnabled(LogLevel logLevel)
            => logLevel >= MinLogLevel;

        public void Log<TState>(LogLevel logLevel, EventId inputEventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel < MinLogLevel)
                return;
            int eventId = inputEventId.Id;
            var message = formatter(state, exception);
            string logLevelString = GetLogLevelString(logLevel);
            _writer.Write(logLevelString);
            _writer.Write(LoglevelPadding);
            if (LogEventId)
            {
                _writer.Write('[');
                Span<char> span = stackalloc char[10];
                if (eventId.TryFormat(span, out int charsWritten))
                    _writer.Write(span.Slice(0, charsWritten));
                else
                    _writer.Write(eventId.ToString());
                _writer.Write(']');
            }

            if (state is IReadOnlyList<KeyValuePair<string, object>> args &&
                IsStructuredLoggerMessage(message, args, out var callerFilePath, out var callerLineNumber, out var trimmedMessage))
            {
                if (LogCaller)
                {
                    _writer.Write(" [");
                    _writer.Write(callerFilePath);
                    _writer.Write('@');
                    _writer.Write(callerLineNumber);
                    _writer.Write(']');
                }
                message = trimmedMessage;
            }

            var now = DateTime.Now;
            _writer.Write(' ');
            _writer.Write(now.ToString("HH:mm:ss.fff"));

            // scope information
            WriteMessage(_writer, message);

            // Example:
            // System.InvalidOperationException
            //    at Namespace.Class.Function() in File:line X
            if (exception != null)
            {
                // exception message
                WriteMessage(_writer, exception.ToString());
            }
            _writer.WriteLine();
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

        private static void WriteMessage(TextWriter textWriter, string message)
        {
            if (!string.IsNullOrEmpty(message))
            {
                textWriter.Write(' ');
                WriteReplacing(textWriter, Environment.NewLine, " ", message);
            }

            static void WriteReplacing(TextWriter writer, string oldValue, string newValue, string message)
            {
                string newMessage = message.Replace(oldValue, newValue);
                writer.Write(newMessage);
            }
        }


        public void Dispose()
        {
            _writer.Dispose();
        }
    }
}
