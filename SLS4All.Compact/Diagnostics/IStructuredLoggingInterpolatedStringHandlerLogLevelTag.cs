// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;

namespace SLS4All.Compact.Diagnostics
{
    public interface IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        static abstract LogLevel Level { get; }
    }

    public struct StructuredLoggingInterpolatedStringHandlerLogLevelNone : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        public static LogLevel Level => LogLevel.None;
    }

    public struct StructuredLoggingInterpolatedStringHandlerLogLevelError : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        public static LogLevel Level => LogLevel.Error;
    }

    public struct StructuredLoggingInterpolatedStringHandlerLogLevelInformation : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        public static LogLevel Level => LogLevel.Information;
    }

    public struct StructuredLoggingInterpolatedStringHandlerLogLevelWarning : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        public static LogLevel Level => LogLevel.Warning;
    }

    public struct StructuredLoggingInterpolatedStringHandlerLogLevelCritical : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        public static LogLevel Level => LogLevel.Critical;
    }

    public struct StructuredLoggingInterpolatedStringHandlerLogLevelDebug : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        public static LogLevel Level => LogLevel.Debug;
    }

    public struct StructuredLoggingInterpolatedStringHandlerLogLevelTrace : IStructuredLoggingInterpolatedStringHandlerLogLevelTag
    {
        public static LogLevel Level => LogLevel.Trace;
    }
}
