using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SkiaSharp;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Controllers;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public class RemainingPrintTimeStylesGeneratorOptions
    {
        public TimeSpan Period { get; set; } = TimeSpan.FromSeconds(0.333);
    }

    public sealed class RemainingPrintTimeStylesGenerator : LazyDataGenerator<RemainingPrintTimeStyles>, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<RemainingPrintTimeStylesGeneratorOptions> _options;
        private readonly IMovementClient _movement;

        public RemainingPrintTimeStylesGenerator(
            ILogger<RemainingPrintTimeStylesGenerator> logger,
            IOptionsMonitor<RemainingPrintTimeStylesGeneratorOptions> options,
            IMovementClient movement)
            : base(logger)
        {
            _logger = logger;
            _options = options;
            _movement = movement;
        }

        protected override async Task RunGeneratorOverride(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            try
            {
                var timer = new PeriodicTimer(options.Period);
                var prevProgress = 0;
                do
                {
                    var printTime = await _movement.GetRemainingPrintTime();
                    var hasXYL = printTime.Flags.HasFlag(RemainingPrintTimeFlags.XYL);
                    var progress = printTime.TotalDuration > TimeSpan.Zero
                        ? (int)Math.Ceiling(100 - (printTime.Duration / printTime.TotalDuration) * 100)
                        : 0;
                    await OnCaptured(new (
                            "visible", 
                            progress, 
                            hasXYL ? "255, 193, 7" : "255, 255, 255", 
                            prevProgress < progress 
                                ? options.Period.TotalSeconds
                                : 0.0),
                        cancel);
                    prevProgress = progress;
                }
                while (await timer.WaitForNextTickAsync(cancel));
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    _logger.LogDebug(ex, $"Failed to process remaining print time");
            }
        }

        protected override void ReleaseTemporaryResources()
        {
        }
    }
}
