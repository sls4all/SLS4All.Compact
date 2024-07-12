// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileSystem;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public class FakeCameraClientOptions
    {
        public int RefreshRate { get; set; } = 30;
    }

    public sealed class FakeCameraClient : IDisposable, ICameraClient
    {
        private readonly ILogger<FakeCameraClient> _logger;
        private readonly IOptionsMonitor<FakeCameraClientOptions> _options;
        private CancellationTokenSource? _cancelSource;
        private Task? _writerFake;

        public AsyncEvent<MimeData> ImageCaptured { get; } = new();

        public bool IsMostlyEmpty => true;

        public FakeCameraClient(
            ILogger<FakeCameraClient> logger,
            IOptionsMonitor<FakeCameraClientOptions> options)
        {
            _logger = logger;
            _options = options;
            ImageCaptured.HandlersChanged += OnHandlersChangedInner;
        }

        private void OnHandlersChangedInner(object? sender, EventArgs e)
        {
            if (ImageCaptured.HandlersCount == 0) // no handlers
            {
                if (_cancelSource != null || _writerFake != null)
                {
                    _logger.LogInformation($"Stopping camera");
                    _cancelSource?.Cancel();
                    _cancelSource = null;
                    _writerFake = null;
                }
            }
            else if (_writerFake == null) // handlers present, not initialized
            {
                _logger.LogInformation($"Starting camera");
                var pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 0, useSynchronizationContext: false)); ;
                var cancelSource = new CancellationTokenSource();
                _writerFake = Task.Run(() => FakeProc(cancelSource.Token));
                _cancelSource = cancelSource;
            }
        }

        private async Task FakeProc(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            var data1 = new MimeData("image/jpeg", GetType().Assembly.GetManifestResourceStream("SLS4All.Compact.Camera.FakeCameraImage.jpg")!.ReadAllToArrayAndDispose());
            var data2 = new MimeData("image/jpeg", GetType().Assembly.GetManifestResourceStream("SLS4All.Compact.Camera.FakeCameraImage2.jpg")!.ReadAllToArrayAndDispose());

            try
            {
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1) / options.RefreshRate);
                while (true)
                {
                    await ImageCaptured.Invoke(data1, cancel);
                    await timer.WaitForNextTickAsync(cancel);
                    await ImageCaptured.Invoke(data2, cancel);
                    await timer.WaitForNextTickAsync(cancel);
                }
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                    _logger.LogError(ex, $"Exception while processing images");
            }
        }

        public void Dispose()
        {
            _cancelSource?.Cancel();
        }

        public Task<IAsyncDisposable> SetCameraMode(CameraMode mode, CancellationToken cancel)
            => Task.FromResult<IAsyncDisposable>(new NullDisposable());
    }
}
