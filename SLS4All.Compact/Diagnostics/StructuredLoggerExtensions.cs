// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

// NOTE: go almost topmost for compiler extension method preference
namespace SLS4All
{
    /// <summary>
    /// Interpolated string handler extensions for efficient formatted and structured logs. Based on <see cref="https://github.com/fedarovich/interpolated-logging-demo"/>
    /// </summary>
    public static class StructuredLoggerExtensions
    {
        public const string CallerFilePathKey = "_callerFilePath";
        public const string CallerLineNumberKey = "_callerLineNumber";

        public static void Log(
            this ILogger logger,
            LogLevel logLevel,
            [InterpolatedStringHandlerArgument("logger", "logLevel")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelNone> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(logLevel, template, arguments);
            }
        }

        public static void Log(
            this ILogger logger,
            LogLevel logLevel,
            Exception? ex,
            [InterpolatedStringHandlerArgument("logger", "logLevel")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelNone> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(logLevel, ex, template, arguments);
            }
        }

        public static void LogError(
            this ILogger logger,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelError> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Error, template, arguments);
            }
        }

        public static void LogError(
            this ILogger logger,
            Exception? ex,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelError> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Error, ex, template, arguments);
            }
        }

        public static void LogInformation(
            this ILogger logger,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelInformation> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Information, template, arguments);
            }
        }

        public static void LogInformation(
            this ILogger logger,
            Exception? ex,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelInformation> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Information, ex, template, arguments);
            }
        }

        public static void LogWarning(
            this ILogger logger,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelWarning> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Warning, template, arguments);
            }
        }

        public static void LogInformation(
            this ILogger logger,
            Exception? ex,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelWarning> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Warning, ex, template, arguments);
            }
        }

        public static void LogCritical(
            this ILogger logger,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelCritical> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Critical, template, arguments);
            }
        }

        public static void LogInformation(
            this ILogger logger,
            Exception? ex,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelCritical> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Critical, ex, template, arguments);
            }
        }

        public static void LogDebug(
            this ILogger logger,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelDebug> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Debug, template, arguments);
            }
        }

        public static void LogDebug(
            this ILogger logger,
            Exception? ex,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelDebug> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Debug, ex, template, arguments);
            }
        }

        public static void LogTrace(
            this ILogger logger,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelTrace> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Trace, template, arguments);
            }
        }

        public static void LogTrace(
            this ILogger logger,
            Exception? ex,
            [InterpolatedStringHandlerArgument("logger")] ref StructuredLoggingInterpolatedStringHandler<StructuredLoggingInterpolatedStringHandlerLogLevelTrace> handler)
        {
            if (handler.IsEnabled)
            {
                var (template, arguments) = handler.FinalizeAndGetTemplateAndArguments();
                logger.Log(LogLevel.Trace, ex, template, arguments);
            }
        }
    }
}
