// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Graphics;
using Microsoft.AspNetCore.Hosting;
using SLS4All.Compact.Camera;
using System.Buffers;
using SLS4All.Compact.Numerics;

namespace SLS4All.Compact.Controllers
{
    public class BedMatrixControllerBox : TemperatureBoxOptions, IOptionsItemEnable
    {
        public bool IsEnabled { get; set; } = true;
    }

    public class BedMatrixControllerOptions : BedMatrixGrabberOptions
    {
    }

    public class BedMatrixControllerQuery
    {
        public bool Cropped { get; set; } = false;
        public UnitConverterFlags Units { get; set; }
    }

    [Route("api/[controller]")]
    [ApiController]
    public class BedMatrixController : ControllerBase
    {
        private readonly static ConcurrentDictionary<(double Average, bool Cropped, UnitConverterFlags Units), BedMatrixGrabber> _grabbers = new();
        private readonly ITemperatureClient _client;
        private readonly ITemperatureCamera _camera;
        private readonly ILogger<BedMatrixController> _logger;
        private readonly IOptionsMonitor<BedMatrixControllerOptions> _options;
        private readonly ImageStreamingHelper _streamingHelper;

        public BedMatrixController(
            ITemperatureClient client,
            ITemperatureCamera camera,
            ILogger<BedMatrixController> logger,
            IOptionsMonitor<BedMatrixControllerOptions> options,
            ImageStreamingHelper streamingHelper)
        {
            _client = client;
            _camera = camera;
            _logger = logger;
            _options = options;
            _streamingHelper = streamingHelper;
        }

        [HttpGet("image/{id}")]
        public Task Image(string id, double seconds, [FromQuery] BedMatrixControllerQuery query, CancellationToken cancel)
        {
            var grabber = GetGrabber(0, query);
            return _streamingHelper.PullImage(
                id,
                grabber.ImageCaptured,
                Response,
                cancel);
        }

        [HttpGet("average/{id}/{seconds}")]
        public Task Average(string id, double seconds, [FromQuery] BedMatrixControllerQuery query, CancellationToken cancel)
        {
            var grabber = GetGrabber(seconds, query);
            return _streamingHelper.PullImage(
                id,
                grabber.ImageCaptured,
                Response,
                cancel);
        }

        private BedMatrixGrabber GetGrabber(double seconds, BedMatrixControllerQuery query)
            => _grabbers.GetOrAdd((seconds, query.Cropped, query.Units), key => new BedMatrixGrabber(
                _logger,
                _options,
                _camera,
                _client,
                seconds,
                query.Cropped,
                query.Units));
    }
}
