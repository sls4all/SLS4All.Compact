// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using SLS4All.Compact.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Diagnostics
{
    public class CompositeLogger(params Span<ILogger> loggers) : ILogger
    {
        private readonly ILogger[] _loggers = (ILogger[])loggers.ToArray();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            if (_loggers.Length == 0)
                return NullDisposable.Instance;
            else if (_loggers.Length == 1)
                return _loggers[0].BeginScope(state);
            else
            {
                var composite = new CompositeDisposable();
                foreach (var logger in _loggers)
                {
                    var scope = logger.BeginScope(state);
                    if (scope != null)
                        composite.Add(scope);
                }
                return composite;
            }
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            foreach (var logger in _loggers)
                if (logger.IsEnabled(logLevel))
                    return true;
            return false;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            foreach (var logger in _loggers)
                logger.Log(logLevel, eventId, state, exception, formatter);
        }
    }
}
