// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public sealed class ProcessOutputHelper : IDisposable, IAsyncDisposable
    {
        private readonly ILogger? _logger;
        private readonly string? _setupBin;
        private readonly string? _setupArgs;
        private readonly string? _captureBin;
        private readonly string? _captureArgs;
        private readonly string? _captureFile;
        private readonly TimeSpan _gracePeriod;
        private readonly Stream? _fakeStream;
        private readonly CancellationTokenSource _cancel;
        private readonly Task _runTask;
        private readonly Func<Stream, CancellationToken, Task>? _processTask;
        private int? _exitCode;

        public Task RunTask => _runTask;
        public int? ExitCode => _exitCode;

        public ProcessOutputHelper(
            ILogger? logger,
            string? setupBin, string? setupArgs,
            string? captureBin, string? captureArgs,
            string? captureFile,
            TimeSpan gracePeriod,
            Stream? fakeStream,
            Func<Stream, CancellationToken, Task>? processTask)
        {
            _logger = logger;
            _setupBin = setupBin;
            _setupArgs = setupArgs;
            _captureBin = captureBin;
            _captureArgs = captureArgs;
            _captureFile = captureFile;
            _gracePeriod = gracePeriod;
            _fakeStream = fakeStream;
            _cancel = new CancellationTokenSource();
            _processTask = processTask;
            _runTask = Task.Factory.StartNew(RunTaskInner, default, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        }

        private async Task RunTaskInner()
        {
            var cancel = _cancel.Token;
            try
            {
                if (!string.IsNullOrWhiteSpace(_setupBin))
                {
                    using (var setup = Process.Start(new ProcessStartInfo
                    {
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = false,
                        RedirectStandardError = false,
                        FileName = _setupBin,
                        Arguments = _setupArgs ?? "",
                    })!)
                    {
                        try
                        {
                            await setup.WaitForExitAsync(cancel);
                        }
                        finally
                        {
                            await SafeKill(setup);
                        }
                    }
                }
                cancel.ThrowIfCancellationRequested();
                if (_captureFile != null)
                {
                    using (var stream = File.Open(_captureFile, FileMode.Open, FileAccess.Read, FileShare.Read))
                    {
                        if (_processTask != null)
                            await _processTask(stream, cancel);
                    }
                }
                else if (_captureBin != null)
                {
                    using (var capture = Process.Start(new ProcessStartInfo
                    {
                        UseShellExecute = false,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        FileName = _captureBin,
                        Arguments = _captureArgs ?? "",
                    })!)
                    {
                        try
                        {
                            // consume error
                            var errorTask = Task.Run(() => capture.StandardError.BaseStream.CopyToAsync(Stream.Null, cancel));
                            // process stdout
                            if (_processTask != null)
                                await _processTask(capture.StandardOutput.BaseStream, cancel);
                            else
                            {
                                var outputTask = Task.Run(() => capture.StandardOutput.BaseStream.CopyToAsync(Stream.Null, cancel));
                            }
                            await capture.WaitForExitAsync(cancel);
                        }
                        finally
                        {
                            await SafeKill(capture);
                            _exitCode = capture.HasExited ? capture.ExitCode : null;
                        }
                    }
                }
                else if (_fakeStream != null)
                {
                    if (_processTask != null)
                        await _processTask(_fakeStream, cancel);
                }
            }
            catch (Exception ex) when (_logger != null)
            {
                if (!cancel.IsCancellationRequested)
                    _logger.LogError(ex, $"Unhandled exception while processing data");
            }
            finally
            {
                if (_fakeStream != null)
                    await _fakeStream.DisposeAsync();
            }
        }

        private async Task SafeKill(Process? process)
        {
            if (process == null)
                return;
            if (process.HasExited)
                return;
            if (OperatingSystem.IsLinux())
                sys_kill(process.Id, 2); // send SIGINT
            else
                process.Kill(false);
            if (process.HasExited)
                return;
            try
            {
                using (var grace = new CancellationTokenSource(_gracePeriod))
                {
                    await process.WaitForExitAsync(grace.Token);
                }
            }
            catch (OperationCanceledException)
            {
                process.Kill(false);
            }
        }

        [DllImport("libc", SetLastError = true, EntryPoint = "kill")]
        private static extern int sys_kill(int pid, int sig);

        public void Dispose()
        {
            _cancel.Cancel();
            _runTask.GetAwaiter().GetResult();
        }

        public async ValueTask DisposeAsync()
        {
            _cancel.Cancel();
            await _runTask;
        }
    }
}
