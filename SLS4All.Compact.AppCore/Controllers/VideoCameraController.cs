// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Temperature;
using SkiaSharp;
using Lexical.FileProvider.Package;
using SLS4All.Compact.IO;
using SLS4All.Compact.Graphics;
using Microsoft.AspNetCore.Authorization;

namespace SLS4All.Compact.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class VideoCameraController : ControllerBase
    {
        private static readonly ConcurrentDictionary<(int, int, BoundaryRectangle), MimeData> _workingAreaBitmaps = new();

        private readonly ICameraClient _client;
        private readonly ILogger<VideoCameraController> _logger;
        private readonly ImageStreamingHelper _streamingHelper;

        public VideoCameraController(
            ICameraClient client,
            ILogger<VideoCameraController> logger,
            ImageStreamingHelper streamingHelper)
        {
            _client = client;
            _logger = logger;
            _streamingHelper = streamingHelper;
        }


        [HttpGet("image/{id}")]
        public Task Image(string id, CancellationToken cancel)
        {
            return _streamingHelper.Pull(
                id,
                _client,
                Response,
                cancel);
        }

        [HttpGet("workingarea/{id}")]
        public Task WorkingArea(string id, CancellationToken cancel)
        {
            var workingAreaNullable = _client.WorkingArea;
            if (workingAreaNullable == null)
                return _streamingHelper.Write(MimeData.TransparentPng, Response, cancel).AsTask();
            var workingArea = workingAreaNullable.Value;
            if (!_workingAreaBitmaps.TryGetValue(workingArea, out var mime))
            {
                using SKBitmap bitmap = new SKBitmap(workingArea.Width, workingArea.Height, false);
                using SKCanvas canvas = new SKCanvas(bitmap);
                canvas.Clear();
                using var pathEffect = SKPathEffect.CreateDash([ 5.0f, 5.0f ], 0f);
                using var paint = new SKPaint
                {
                    IsStroke = true,
                    StrokeWidth = 1,
                    Color = SKColors.Purple.WithAlpha(128),
                    PathEffect = pathEffect,
                };
                canvas.DrawRect(
                    new SKRect(workingArea.Working.MinX, workingArea.Working.MinY, workingArea.Working.MaxX, workingArea.Working.MaxY),
                    paint);
                canvas.Flush();
                using var data = bitmap.Encode(SKEncodedImageFormat.Png, 100);
                mime = new MimeData("image/png", data.ToArray());
                if (!_workingAreaBitmaps.TryAdd(workingArea, mime))
                    mime = _workingAreaBitmaps[workingArea];
            }
            return _streamingHelper.Write(mime, Response, cancel).AsTask();
        }
    }
}
