// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.IO;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;

namespace SLS4All.Compact.Diagnostics
{
    /// <summary>
    /// Interpolated string handler for efficient formatted and structured logs. Based on <see cref="https://github.com/fedarovich/interpolated-logging-demo"/>
    /// </summary>
    [InterpolatedStringHandler]
    public ref struct StructuredLoggingInterpolatedStringHandler<TLevel>
        where TLevel : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        private const int _maxApproximateArgumentNameLength = 20;
        private const int _callerArgumentCount = 2;
        private readonly StringBuilder? _template;
        private readonly object?[]? _arguments;
        private readonly string? _callerFilePath;
        private readonly int _callerLineNumber;
        private readonly bool _isEnabled;
        private readonly char[] _expressionChars = new[] { '(', ')', '\'', '"', ' ' };
        private int _argumentIndex;

        public bool IsEnabled => _isEnabled;

        public StructuredLoggingInterpolatedStringHandler(int literalLength, int formattedCount, ILogger logger, out bool isEnabled, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
            : this(literalLength, formattedCount, logger, TLevel.Level, out isEnabled, callerFilePath, callerLineNumber)
        {
        }

        public StructuredLoggingInterpolatedStringHandler(int literalLength, int formattedCount, ILogger logger, LogLevel logLevel, out bool isEnabled, [CallerFilePath] string callerFilePath = "", [CallerLineNumber] int callerLineNumber = 0)
        {
            isEnabled = logger.IsEnabled(logLevel);
            if (isEnabled)
            {
                var argCount = formattedCount + _callerArgumentCount;
                _template = new StringBuilder(literalLength + _maxApproximateArgumentNameLength * argCount);
                _arguments = new object?[argCount];
                _callerFilePath = callerFilePath;
                _callerLineNumber = callerLineNumber;
                _isEnabled = true;
            }
        }

        public void AppendLiteral(string s)
        {
            if (!IsEnabled)
                return;

            var start = _template!.Length;
            _template.Append(s);
            _template.Replace("{", "{{", start, _template.Length - start);
            _template.Replace("}", "}}", start, _template.Length - start);
        }

        public void AppendFormatted<T>(T value, [CallerArgumentExpression("value")] string name = "", string? format = null)
        {
            if (!IsEnabled)
                return;

            _template!.Append("{");
            if (name.IndexOfAny(_expressionChars) != -1)
                name = $"expression{_argumentIndex}";
            _template.Append(name);
            _template.Append('}');
            if (format != null && value is IFormattable formattable)
                AddArgumentInternal(formattable.ToString(format, null));
            else
                AddArgumentInternal(value);
        }

        private void AddArgumentInternal(object? value)
            => _arguments![_argumentIndex++] = value;

        public (string, object?[]) FinalizeAndGetTemplateAndArguments()
        {
            const string callerTemplate = $" [{{{StructuredLoggerExtensions.CallerFilePathKey}}}@{{{StructuredLoggerExtensions.CallerLineNumberKey}}}]";
            _template!.Append(callerTemplate);
            AddArgumentInternal(CompactPathExtensions.GetFileNameOSUniversal(_callerFilePath));
            AddArgumentInternal(_callerLineNumber);
            return (_template.ToString(), _arguments!);
        }
    }
}
