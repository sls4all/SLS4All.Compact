// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public abstract class LazyImageGenerator : IDisposable, IImageGenerator
    {
        private readonly ILogger _logger;
        private readonly Lock _lastMimeLock = new();
        private MimeData _lastMime;
        private bool _grabberRunning;
        private CancellationTokenSource _cancelSource;
        private Task? _grabberTask;

        protected CancellationTokenSource CancelSource => _cancelSource;
        public AsyncEvent<MimeData> ImageCaptured { get; } = new();

        public LazyImageGenerator(
            ILogger logger)
        {
            _logger = logger;
            _cancelSource = new();

            ImageCaptured.HandlersChanged += OnHandlersChangedInner;
        }

        protected abstract void ReleaseTemporaryResources();
        
        protected void RestartGrabberIfRunning()
        {
            lock (ImageCaptured.HandlersChangedSync)
            {
                if (_grabberTask != null)
                {
                    _logger.LogInformation($"Restarting grabber");
                    _cancelSource.Cancel();
                    if (_grabberTask != null)
                    {
                        _grabberTask.GetAwaiter().GetResult();
                        _grabberTask = null;
                    }
                    var cancelSource = new CancellationTokenSource();
                    _cancelSource = cancelSource;
                    _grabberTask = Task.Factory.StartNew(
                        state => RunGrabber(cancelSource.Token),
                        null,
                        default,
                        TaskCreationOptions.LongRunning,
                        TaskScheduler.Default).Unwrap();
                }
            }
        }

        private void OnHandlersChangedInner(object? sender, EventArgs e)
        {
            if (ImageCaptured.HandlersCount == 0) // no handlers
            {
                _logger.LogInformation($"Stopping grabber");
                _cancelSource.Cancel();
                if (_grabberTask != null)
                {
                    _grabberTask.GetAwaiter().GetResult();
                    _grabberTask = null;
                }
                ReleaseTemporaryResources();
            }
            else if (_grabberTask == null) // handlers present, not initialized
            {
                _logger.LogInformation($"Starting grabber");
                var cancelSource = new CancellationTokenSource();
                _cancelSource = cancelSource;

                _grabberTask = Task.Factory.StartNew(
                    state => RunGrabber(cancelSource.Token),
                    null,
                    default,
                    TaskCreationOptions.LongRunning,
                    TaskScheduler.Default).Unwrap();
            }
        }

        private async Task RunGrabber(CancellationToken cancel)
        {
            try
            {
                lock (_lastMimeLock)
                {
                    _grabberRunning = true;
                }
                cancel.ThrowIfCancellationRequested();
                await RunGrabberOverride(cancel);
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    _logger.LogError(ex, $"Failed to start/run grabber task");
            }
            finally
            {
                lock (_lastMimeLock)
                {
                    _grabberRunning = false;
                    _lastMime = default;
                }
            }
        }

        protected ValueTask OnImageCaptured(MimeData data, CancellationToken cancel)
        {
            lock (_lastMimeLock)
            {
                if (_grabberRunning)
                    _lastMime = data;
            }
            return ImageCaptured.Invoke(data, cancel);
        }

        protected abstract Task RunGrabberOverride(CancellationToken cancel);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            _cancelSource.Cancel();
            ReleaseTemporaryResources();
        }

        public bool TryGetLastImage(out MimeData data)
        {
            lock (_lastMimeLock)
            {
                data = _lastMime;
            }
            return !data.IsEmpty;
        }
    }
}
