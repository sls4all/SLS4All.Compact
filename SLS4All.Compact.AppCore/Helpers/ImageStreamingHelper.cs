// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Azure;
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
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SLS4All.Compact.Helpers
{
    public sealed class ImageStreamingHelper : StreamingHelperBase<MimeData>
    {
        private static readonly MimeData _imageStreamingPlaceholder;

        static ImageStreamingHelper()
        {
            _imageStreamingPlaceholder = new MimeData(
                "image/gif",
                typeof(ImageStreamingHelper).Assembly.GetManifestResourceStream("SLS4All.Compact.Helpers.ImageStreamingPlaceholder.gif")!.ReadAllToArrayAndDispose());
        }

        public ImageStreamingHelper(
            ILogger<ImageStreamingHelper> logger,
            IOptionsMonitor<StreamingHelperOptions> options)
            : base(logger, options, _imageStreamingPlaceholder)
        {
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

        public override ValueTask Write(MimeData data, HttpResponse response, CancellationToken cancel)
        {
            response.ContentType = data.ContentType;
            response.Headers["Cache"] = "no-store, no-cache, must-revalidate";
            return response.Body.WriteAsync(data.Data, cancel);
        }

        protected override MimeData RentCopy(MimeData data)
            => data.RentCopy();

        protected override void Return(MimeData data)
            => data.Return();

        protected override bool IsEmpty(MimeData data)
            => data.IsEmpty;
    }
}
