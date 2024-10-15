// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Controllers;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.Intrinsics.X86;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace SLS4All.Compact.Temperature
{
    public class BedMatrixGrabberOptions
    {
        public float TextSize { get; set; } = 13.0f;
        public int Scale { get; set; } = 30;
        public int XOffset { get; set; } = 1;
        public int YOffset { get; set; } = 10;
        public int Quality { get; set; } = 90;
        public Dictionary<string, BedMatrixControllerBox?>? Boxes { get; set; }
        public float PathDashLength { get; set; } = 20;
    }

    public sealed class BedMatrixGrabber : ImageGrabber
    {
        public const string ImageMime = "image/jpeg";
        private readonly ILogger _logger;
        private readonly IOptionsMonitor<BedMatrixGrabberOptions> _options;
        private readonly ITemperatureCamera _camera;
        private readonly double _average;
        private readonly bool _cropped;
        private readonly UnitConverterFlags _units;
        private readonly double _refreshRate;

        private readonly object _locker = new();
        private readonly SKTypeface _typeface;
        private readonly SKFont _font;
        private SKBitmap? _bitmap;
        private SKBitmap? _bitmapScaled;
        private SKBitmap? _bitmapCropped;
        private int _lastValuesWidth;
        private int _lastValuesHeight;
        private int _lastBoxWidth;
        private int _lastBoxHeight;

        public BedMatrixGrabber(
            ILogger logger,
            IOptionsMonitor<BedMatrixGrabberOptions> options,
            ITemperatureCamera camera,
            double average,
            bool cropped,
            UnitConverterFlags units,
            double refreshRate)
            : base(logger)
        {
            _logger = logger;
            _options = options;
            _camera = camera;
            _average = average;
            _cropped = cropped;
            _units = units;
            _refreshRate = refreshRate;

            var o = _options.CurrentValue;
            using (var stream = GetType().Assembly.GetManifestResourceStream("SLS4All.Compact.Temperature.BedMatrixGrabber.ttf")!)
                _typeface = SKTypeface.FromStream(stream);
            _font = new SKFont(_typeface);
        }

        private static float[] CalcAvgValues(PrimitiveDeque<(TimeSpan elapsed, float[] matrix)> queue, float[] avg)
        {
            ref var first = ref queue.PeekFront();
            if (avg.Length != first.matrix.Length)
                avg = new float[first.matrix.Length];
            var c = queue.Count;
            if (c == 1)
            {
                queue[0].matrix.CopyTo(avg, 0);
            }
            else
            {
                Array.Clear(avg);
                for (int i = 0; i < c; i++)
                {
                    var matrix = queue[i].matrix;
                    for (int q = 0; q < avg.Length; q++)
                        avg[q] += matrix[q];
                }
                for (int q = 0; q < avg.Length; q++)
                    avg[q] /= c;
            }
            return avg;
        }

        protected override async Task RunGrabberOverride(CancellationToken cancel)
        {
            var queue = new PrimitiveDeque<(TimeSpan elapsed, float[] matrix)>(1);
            var stopwatch = Stopwatch.StartNew();
            ValueTask OnPixelsChanged(CancellationToken cancel)
            {
                var elapsed = stopwatch.Elapsed;
                var clone = (float[])_camera.CurrentPixels.Clone();
                lock (queue)
                {
                    while (queue.Count > 0 && (elapsed - queue.PeekFront().elapsed).TotalSeconds >= _average)
                        queue.PopFront();
                    queue.PushBack() = (elapsed, clone);
                }
                return ValueTask.CompletedTask;
            }
            var stream = new MemoryStream();
            var chain = new TaskQueue();
            var options = _options.CurrentValue;
            try
            {
                _camera.CurrentPixelsChanged.AddHandler(OnPixelsChanged);

                var timer = new PeriodicTimer(TimeSpan.FromSeconds(1) / _refreshRate);
                float[] avg = [];
                do
                {
                    if (queue.Count > 0)
                    {
                        lock (queue)
                        {
                            avg = CalcAvgValues(queue, avg);
                        }
                        stream.SetLength(0);
                        WriteImage(_cropped, avg, _camera.Width, _camera.Height, stream);
                        await ImageCaptured.Invoke(new MimeData(ImageMime, stream.AsMemory()), cancel);
                    }
                }
                while (await timer.WaitForNextTickAsync(cancel));
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested && ex is not ObjectDisposedException)
                    _logger.LogDebug(ex, $"Failed to process temperature");
            }
            finally
            {
                _camera.CurrentPixelsChanged.RemoveHandler(OnPixelsChanged);
            }
        }

        private unsafe static void PutPixel(byte* pixels, int stride, int x, int y, float v, float vmin, float vrange)
        {
            var c = ColorHelper.GetFullHeatmapColor(v, vmin, vrange);
            int offset = y * stride + x * 2;
            var pixel = (ushort*)(pixels + offset);
            *pixel = c.To24Bits();
        }

        public void WriteImage(
            bool cropped,
            float[] values,
            int valuesWidth,
            int valuesHeight,
            Stream body)
        {
            var options = _options.CurrentValue;
            SKData? data = null;
            try
            {
                lock (_locker)
                {
                    var changedValuesSize = valuesWidth != _lastValuesWidth || valuesHeight != _lastValuesHeight;
                    _lastValuesWidth = valuesWidth;
                    _lastValuesHeight = valuesHeight;

                    var vmin = double.MaxValue;
                    var vmax = double.MinValue;
                    foreach (var v in values)
                    {
                        if (v < vmin)
                            vmin = v;
                        if (v > vmax)
                            vmax = v;
                    }
                    var vrange = vmax - vmin;

                    if (changedValuesSize || _bitmap == null)
                    {
                        _bitmap?.Dispose();
                        _bitmap = new SKBitmap(valuesWidth, valuesHeight, SKColorType.Rgb565, SKAlphaType.Opaque);
                    }
                    unsafe
                    {
                        var pixels = (byte*)_bitmap.GetPixels();
                        var rowBytes = _bitmap.RowBytes;
                        for (int y = 0; y < valuesHeight; y++)
                        {
                            for (int x = 0; x < valuesWidth; x++)
                            {
                                PutPixel(
                                    pixels, rowBytes, x, y,
                                    values[x + y * valuesWidth],
                                    (float)vmin,
                                    (float)vrange);
                            }
                        }
                    }
                    int scale = options.Scale;
                    if (changedValuesSize || _bitmapScaled == null)
                    {
                        _bitmapScaled?.Dispose();
                        _bitmapScaled = new SKBitmap(valuesWidth * scale, valuesHeight * scale, SKColorType.Rgb565, SKAlphaType.Opaque);
                    }
                    using var canvas = new SKCanvas(_bitmapScaled);
                    using var paint = new SKPaint(_font);
                    var xoffset = options.XOffset;
                    var yoffset = options.YOffset;
                    paint.TextSize = options.TextSize;
                    paint.TextAlign = SKTextAlign.Right;
                    _bitmap.ScalePixels(_bitmapScaled, SKFilterQuality.Low); // NOTE: low means bilinear, which is good enough

                    using var pathEffect1 = SKPathEffect.CreateDash(new[] { options.PathDashLength, options.PathDashLength }, 0f);
                    using var pathEffect2 = SKPathEffect.CreateDash(new[] { options.PathDashLength, options.PathDashLength }, options.PathDashLength);
                    using var boxPaint1 = new SKPaint { Color = SKColors.White, StrokeWidth = 1f, IsStroke = true, IsAntialias = true };
                    using var boxPaint2 = new SKPaint { Color = SKColors.Black, StrokeWidth = 1f, IsStroke = true, IsAntialias = true };
                    boxPaint1.PathEffect = pathEffect1;
                    boxPaint2.PathEffect = pathEffect2;
                    var boxes = options.Boxes?.GetOrderedEnabledValues() ?? Array.Empty<BedMatrixControllerBox>();
                    var mainBox = _camera.MainBox;
                    foreach (var box in boxes)
                    {
                        var box2 = mainBox.OffsetInTopLeft(box.Box);
                        canvas.DrawRect(box2.MinX * scale, box2.MinY * scale, (box2.MaxX - box2.MinX + 1) * scale - 1, (box2.MaxY - box2.MinY + 1) * scale - 1, boxPaint1);
                        canvas.DrawRect(box2.MinX * scale, box2.MinY * scale, (box2.MaxX - box2.MinX + 1) * scale - 1, (box2.MaxY - box2.MinY + 1) * scale - 1, boxPaint2);
                    }
                    for (int y = 0; y < valuesHeight; y++)
                    {
                        for (int x = 0; x < valuesWidth; x++)
                        {
                            var v = values[x + y * valuesWidth];
                            var c = ColorHelper.GetFullHeatmapColor((float)v, (float)vmin, (float)vrange);
                            var ca = (c.R + c.G + c.B) / 3 >= 128 ? (byte)0 : (byte)255;
                            paint.Color = new SKColor(ca, ca, ca);
                            var temperature = (_units & UnitConverterFlags.PreferFahrenheit) != 0 ? v * 1.8f + 32.0f : v;
                            canvas.DrawText(temperature.ToString("0.0"), (x + 1) * scale - 1 - xoffset, (y + 1) * scale - 1 - yoffset, paint);
                        }
                    }
                    canvas.Flush();

                    if (cropped)
                    {
                        var changedBoxSize = mainBox.Width != _lastBoxWidth || mainBox.Height != _lastBoxHeight;
                        _lastBoxWidth = mainBox.Width;
                        _lastBoxHeight = mainBox.Height;
                        if (changedBoxSize || _bitmapCropped == null)
                        {
                            _bitmapCropped?.Dispose();
                            _bitmapCropped = new SKBitmap(mainBox.Width * scale, mainBox.Height * scale, SKColorType.Rgb565, SKAlphaType.Opaque);
                        }
                        using var canvasCropped = new SKCanvas(_bitmapCropped);
                        canvasCropped.DrawBitmap(
                            _bitmapScaled,
                            new SKRect(mainBox.MinX * scale, mainBox.MinY * scale, (mainBox.MaxX + 1) * scale, (mainBox.MaxY + 1) * scale),
                            new SKRect(0, 0, mainBox.Width * scale, mainBox.Height * scale));
                        canvasCropped.Flush();
                        _bitmapCropped.Encode(body, SKEncodedImageFormat.Jpeg, options.Quality);
                    }
                    else
                    {
                        _bitmapScaled.Encode(body, SKEncodedImageFormat.Jpeg, options.Quality);
                    }
                }
            }
            finally
            {
                data?.Dispose();
            }
        }

        protected override void ReleaseTemporaryResources()
        {
            lock (_locker)
            {
                _bitmap?.Dispose();
                _bitmap = null;
                _bitmapScaled?.Dispose();
                _bitmapScaled = null;
                _bitmapCropped?.Dispose();
                _bitmapCropped = null;
            }
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _typeface.Dispose();
            _font.Dispose();
        }
    }
}
