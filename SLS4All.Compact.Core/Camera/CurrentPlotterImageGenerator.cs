// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel.Design.Serialization;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public class PlotterImageGeneratorOptions
    {
        public TimeSpan Period { get; set; } = TimeSpan.FromSeconds(0.5);
        public TimeSpan HotspotAge { get; set; } = TimeSpan.FromSeconds(0.5);
    }

    public sealed class CurrentPlotterImageGenerator : BackgroundThreadService, ICurrentPlotterImageGenerator
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<PlotterImageGeneratorOptions> _options;
        private readonly ICodePlotter _plotter;
        private readonly Lock _lastMimeLock = new();
        private MimeData _lastMime;

        public AsyncEvent<MimeData> Captured { get; } = new();

        public CurrentPlotterImageGenerator(
            ILogger<CurrentPlotterImageGenerator> logger,
            IOptionsMonitor<PlotterImageGeneratorOptions> options,
            ICodePlotter plotter) 
            : base(logger)
        {
            _logger = logger;
            _plotter = plotter;
            _options = options;
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            var timer = new PeriodicTimer(options.Period);
            var lastVersion = long.MinValue;
            var borrowedFrames = 0;
            while (await timer.WaitForNextTickAsync(cancel))
            {
                options = _options.CurrentValue;
                if (timer.Period != options.Period)
                    timer = new PeriodicTimer(options.Period);
                try
                {
                    var version = _plotter.Version;
                    if (version == lastVersion)
                    {
                        if (borrowedFrames == 0)
                            continue;
                        borrowedFrames--;
                    }
                    else
                        borrowedFrames = (int)((options.HotspotAge.Ticks + options.Period.Ticks - 1) / options.Period.Ticks) + 1; // to fade out hotspot
                    lastVersion = version;
                    var image = _plotter.CreateImage(hotspotAge: options.HotspotAge, hotspotTo: SystemTimestamp.Now, noCache: true);
                    lock (_lastMimeLock)
                    {
                        _lastMime.Return();
                        _lastMime = image.RentCopy();
                    }
                    await Captured.Invoke(image, cancel);
                }
                catch (Exception ex) when (!cancel.IsCancellationRequested)
                {
                    _logger.LogDebug(ex, "Failed to process and post image");
                }
            }
        }

        public bool TryRentLastValue(out MimeData data)
        {
            lock (_lastMimeLock)
            {
                data = _lastMime.RentCopy();
                return !data.IsEmpty;
            }
        }

        public IDisposable StartScope()
            => NullDisposable.Instance;
    }
}
