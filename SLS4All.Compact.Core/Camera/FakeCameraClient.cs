// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
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

    public sealed class FakeCameraClient : LazyDataGenerator<MimeData>, ICameraClient
    {
        private readonly ILogger<FakeCameraClient> _logger;
        private readonly IOptionsMonitor<FakeCameraClientOptions> _options;
        private readonly MimeData _data1, _data2;

        public bool IsMostlyEmpty => true;
        public (int Width, int Height, BoundaryRectangle Working)? WorkingArea { get; }

        public FakeCameraClient(
            ILogger<FakeCameraClient> logger,
            IOptionsMonitor<FakeCameraClientOptions> options)
            : base(logger)
        {
            _logger = logger;
            _options = options;

            _data1 = new MimeData("image/jpeg", GetType().Assembly.GetManifestResourceStream("SLS4All.Compact.Camera.FakeCameraImage.jpg")!.ReadAllToArrayAndDispose());
            _data2 = new MimeData("image/jpeg", GetType().Assembly.GetManifestResourceStream("SLS4All.Compact.Camera.FakeCameraImage2.jpg")!.ReadAllToArrayAndDispose());
            WorkingArea = (720, 720, new BoundaryRectangle(10, 10, 710, 710));
        }

        protected override async Task RunGeneratorOverride(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            try
            {
                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1) / options.RefreshRate);
                while (true)
                {
                    await OnCaptured(_data1, cancel);
                    await timer.WaitForNextTickAsync(cancel);
                    await OnCaptured(_data2, cancel);
                    await timer.WaitForNextTickAsync(cancel);
                }
            }
            catch (Exception ex)
            {
                if (ex is not OperationCanceledException)
                    _logger.LogError(ex, $"Exception while processing images");
            }
        }

        public Task<IAsyncDisposable> SetCameraMode(CameraMode mode, CancellationToken cancel)
            => Task.FromResult<IAsyncDisposable>(new NullDisposable());

        public (int RequiredLength, int Width, int Height) TryGetWorkingAreaBrightness(Span<byte> pixels)
        {
            var workingArea = WorkingArea!.Value.Working;
            var count = workingArea.Width * workingArea.Height;
            if (pixels.Length < count)
                return (count, 0, 0);
            else
            {
                var random = new Random(1234);
                random.NextBytes(pixels.Slice(0, count));
                return (count, workingArea.Width, workingArea.Height);
            }
        }

        protected override void ReleaseTemporaryResources()
        {
        }

        protected override MimeData RentCopy(MimeData data)
            => data.RentCopy();

        protected override void Return(MimeData data)
            => data.Return();

        protected override bool IsEmpty(MimeData data)
            => data.IsEmpty;
    }
}
