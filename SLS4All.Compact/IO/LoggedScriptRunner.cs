// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection.Metadata;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static System.Collections.Specialized.BitVector32;

namespace SLS4All.Compact.IO
{
    public class LoggedScriptRunnerOptions
    {
        public TimeSpan GracePeriod { get; set; } = TimeSpan.FromSeconds(5);
        public TimeSpan OutputDelay { get; set; } = TimeSpan.FromSeconds(0.25);
        public int OutputMaxLines { get; set; } = 100;
        public bool LogOutput { get; set; } = true;

        public string ExecutableLinux { get; set; } = "";
        public string ExecutableWindows { get; set; } = "";
        public string Executable { get; set; } = "";
        public string ExecutablePlatform
        {
            get
            {
                var executable = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ExecutableWindows : ExecutableLinux;
                if (!string.IsNullOrEmpty(executable))
                    return executable;
                else
                    return Executable;
            }
        }
        public string ArgsLinux { get; set; } = "";
        public string ArgsWindows { get; set; } = "";
        public string Args { get; set; } = "";
        public string ArgsPlatform
        {
            get
            {
                var args = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ArgsWindows : ArgsLinux;
                if (!string.IsNullOrEmpty(args))
                    return args;
                else
                    return Args;
            }
        }
    }

    public abstract class LoggedScriptRunner<TOutputCategoryName>
    {
        private readonly ILogger _logger;
        private readonly ILogger<TOutputCategoryName> _outputLogger;
        private readonly IOptionsMonitor<LoggedScriptRunnerOptions> _options;

        protected LoggedScriptRunner(
            ILogger logger,
            ILogger<TOutputCategoryName> outputLogger,
            IOptionsMonitor<LoggedScriptRunnerOptions> options)
        {
            _logger = logger;
            _outputLogger = outputLogger;
            _options = options;
        }

        protected async Task<int?> Run(string filename, string args, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            var options = _options.CurrentValue;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                try
                {
                    var oldFileMode = File.GetUnixFileMode(filename);
                    var newFileMode = oldFileMode | UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                    if (newFileMode != oldFileMode)
                        File.SetUnixFileMode(filename, newFileMode);
                }
                catch
                {
                    if (Path.GetExtension(filename).Equals(".sh", StringComparison.InvariantCultureIgnoreCase))
                    {
                        args = $"{filename} {args}";
                        filename = "/bin/bash";
                    }
                }
            }
            _logger.LogInformation($"Starting script/binary: {filename} {args}");
            using (var helper = new ProcessOutputHelper(
                _logger,
                null,
                null,
                filename,
                args,
                null,
                options.GracePeriod,
                null,
                async (stream, cancel) =>
                {
                    using (var reader = new StreamReader(stream))
                    {
                        var buf = new StringBuilder();
                        var bufLines = 0;
                        void FlushBuf()
                        {
                            lock (buf)
                            {
                                while (buf.Length > 0 && buf[^1] is '\n' or '\r')
                                    buf.Length--;
                                if (buf.Length > 0)
                                {
                                    var options = _options.CurrentValue;
                                    if (options.LogOutput)
                                        _outputLogger.LogDebug(buf.ToString());
                                    buf.Clear();
                                }
                            }
                        }
                        using (var timer = new Timer(state => FlushBuf()))
                        {
                            while (true)
                            {
                                var line = await reader.ReadLineAsync(cancel);
                                if (line == null)
                                    break;
                                lock (buf)
                                {
                                    buf.AppendLine(line);
                                    if (++bufLines >= options.OutputMaxLines)
                                        FlushBuf();
                                    else
                                        timer.Change(options.OutputDelay, Timeout.InfiniteTimeSpan);
                                }
                            }
                        }
                    }
                }))
            using (cancel.Register(helper.Dispose))
            {
                await helper.RunTask;
                return helper.ExitCode;
            }
        }
    }
}
