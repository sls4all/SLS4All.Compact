// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Lexical.FileProvider.Package;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public class ImageStreamingHelperOptions
    {
        public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromSeconds(5);
    }

    public class ImageStreamingHelper : BackgroundThreadService
    {
        private sealed class PullData
        {
            public required AsyncEvent<MimeData> ImageCapturedEvent { get; init; }
            public SystemTimestamp Timestamp;
            public MimeData Data;
            public Func<MimeData, CancellationToken, ValueTask>? Handler;
        }

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<ImageStreamingHelperOptions> _options;
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _streamCancels;
        private readonly object _locker;
        private readonly Dictionary<AsyncEvent<MimeData>, PullData> _pullDataByEvent;
        private readonly Dictionary<string, PullData> _pullDataById;
        private readonly MimeData _imageStreamingPlaceholder;

        public ImageStreamingHelper(
            ILogger<ImageStreamingHelper> logger,
            IOptionsMonitor<ImageStreamingHelperOptions> options)
            : base(logger)
        {
            _logger = logger;
            _options = options;

            _streamCancels = new();
            _locker = new();
            _pullDataByEvent = new();
            _pullDataById = new();
            _imageStreamingPlaceholder = new MimeData(
                "image/gif",
                GetType().Assembly.GetManifestResourceStream("SLS4All.Compact.Helpers.ImageStreamingPlaceholder.gif")!.ReadAllToArrayAndDispose());
        }

        public async Task StreamMultipart(
            ILogger logger,
            AsyncEvent<MimeData> imageCaptured,
            HttpResponse response,
            string mime,
            string id,
            CancellationToken cancel)
        {
            var boundaryStr = "F89DDD02-E3ED-4E9C-AFC3-80CEA815BA72";
            var boundaryAndHeadersStr = "--" + boundaryStr + "\r\nContent-Type: " + mime + "\r\n\r\n";
            var firstBoundaryBytes = Encoding.ASCII.GetBytes(boundaryAndHeadersStr);
            var otherBoundaryBytes = Encoding.ASCII.GetBytes("\r\n--" + boundaryAndHeadersStr);
            response.Headers["Cache"] = "no-store, no-cache, must-revalidate";
            response.ContentType = "multipart/x-mixed-replace;boundary=" + boundaryStr;
            var cancelSource = new CancellationTokenSource();
            var collapseTask = new BackgroundTask(noStatus: true);
            Task OnImageCaptured(MimeData image, CancellationToken cancel)
            {
                var imageCopy = image.Data.Span.ToBorrowedArrayMemory();
                return collapseTask.StartTask(null, cancel => Task.Run(async () =>
                {
                    try
                    {
                        await response.Body.WriteAsync(imageCopy, cancel);
                        await response.Body.WriteAsync(otherBoundaryBytes, cancel);
                        await response.Body.FlushAsync(cancel);
                    }
                    catch (Exception ex)
                    {
                        if (ex is not ObjectDisposedException)
                            logger.LogDebug(ex, $"Failed to write response, will cancel: {id}");
                        cancelSource.Cancel();
                    }
                    finally
                    {
                        PrinterMemoryExtensions.ReturnArrayMemory<byte>(imageCopy);
                    }
                }), cancel =>
                {
                    PrinterMemoryExtensions.ReturnArrayMemory<byte>(imageCopy);
                    return Task.CompletedTask;
                });
            };
            logger.LogDebug($"Starting multipart streaming: {id}");
            if (!_streamCancels.TryAdd(id, cancelSource))
                return;
            using (var combined = CancellationTokenSource.CreateLinkedTokenSource(cancel, cancelSource.Token))
            {
                try
                {
                    await response.StartAsync(combined.Token);
                    await response.Body.WriteAsync(firstBoundaryBytes, combined.Token);
                    imageCaptured.AddHandler(OnImageCaptured);

                    var source = new TaskCompletionSource();
                    using (combined.Token.Register(() => source.TrySetResult()))
                    {
                        await source.Task;
                    }
                }
                catch (Exception ex)
                {
                    if (!combined.IsCancellationRequested)
                        logger.LogDebug(ex, $"Exception during multipart streaming: {id}");
                    throw;
                }
                finally
                {
                    logger.LogDebug($"Ending multipart streaming: {id}");
                    imageCaptured.RemoveHandler(OnImageCaptured);
                    _streamCancels.TryRemove(id, out _);
                    collapseTask.Cancel();
                }
            }
        }

        public bool TryGetImageCapturedEvent(string id, [MaybeNullWhen(false)] out AsyncEvent<MimeData> imageReadyEvent)
        {
            lock (_locker)
            {
                if (_pullDataById.TryGetValue(id, out var pullData))
                {
                    imageReadyEvent = pullData.ImageCapturedEvent;
                    return true;
                }
                else
                {
                    imageReadyEvent = null;
                    return false;
                }
            }
        }

        public void SetPulledImage(string id)
        {
            lock (_locker)
            {
                if (_pullDataById.TryGetValue(id, out var pullData))
                    pullData.Timestamp = SystemTimestamp.Now;
            }
        }

        public async Task PullImage(
            string id,
            AsyncEvent<MimeData> imageCaptured,
            HttpResponse response,
            CancellationToken cancel)
        {
            PullData? pullData;

            var now = SystemTimestamp.Now;
            var data = _imageStreamingPlaceholder;

            lock (_locker)
            {
                if (!_pullDataByEvent.TryGetValue(imageCaptured, out pullData))
                {
                    if (!_pullDataByEvent.TryGetValue(imageCaptured, out pullData))
                    {
                        pullData = new PullData
                        {
                            ImageCapturedEvent = imageCaptured,
                        };
                        _pullDataByEvent.TryAdd(imageCaptured, pullData);
                    }
                }

                _pullDataById[id] = pullData;

                pullData.Timestamp = now;

                if (pullData.Handler == null)
                {
                    pullData.Handler = (image, cancel) =>
                    {
                        var imageCopy = image.RentCopy();
                        lock (_locker)
                        {
                            pullData.Data.Return();
                            pullData.Data = imageCopy;
                        }
                        return ValueTask.CompletedTask;
                    };

                    imageCaptured.AddHandler(pullData.Handler);
                }

                if (!pullData.Data.IsEmpty)
                    data = pullData.Data.RentCopy();
            }

            response.ContentType = data.ContentType;
            response.Headers["Cache"] = "no-store, no-cache, must-revalidate";
            await response.Body.WriteAsync(data.Data, cancel);
            data.Return();
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            var timer = new PeriodicTimer(options.CleanupPeriod);
            var remainingDataById = new HashSet<PullData>();
            var removedIds = new HashSet<string>();
            var removedEvents = new HashSet<AsyncEvent<MimeData>>();
            while (await timer.WaitForNextTickAsync(cancel))
            {
                var expiredAt = SystemTimestamp.Now - options.CleanupPeriod;
                remainingDataById.Clear();
                removedIds.Clear();
                removedEvents.Clear();
                lock (_locker)
                {
                    foreach ((var id, var pullData) in _pullDataById)
                    {
                        if (pullData.Timestamp < expiredAt || pullData.Handler == null)
                            removedIds.Add(id);
                    }

                    foreach (var id in removedIds)
                        _pullDataById.Remove(id);

                    foreach (var pullData in _pullDataById.Values)
                        remainingDataById.Add(pullData);

                    foreach ((var ev, var pullData) in _pullDataByEvent)
                    {
                        if (!remainingDataById.Contains(pullData))
                        {
                            if (pullData.Handler != null)
                            {
                                pullData.ImageCapturedEvent.RemoveHandler(pullData.Handler);
                                pullData.Handler = null;
                            }
                            removedEvents.Add(ev);
                        }
                    }

                    foreach (var ev in removedEvents)
                        _pullDataByEvent.Remove(ev);
                }
            }
        }
    }
}
