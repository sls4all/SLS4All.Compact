// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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
using SLS4All.Compact.Slicing;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using static System.Net.Mime.MediaTypeNames;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Camera;

namespace SLS4All.Compact.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PlottedImageController : ControllerBase
    {
        private readonly static Lock _singleThreadedCreateImageLock = new(); // helps to reduce CPU load when multiple clients are connected and requesting plots
        private readonly ImageStreamingHelper _streamingHelper;
        private readonly ICurrentPlotterImageGenerator _generator;
        private readonly ICodePlotter _plotter;

        public PlottedImageController(
            ImageStreamingHelper streamingHelper,
            ICurrentPlotterImageGenerator generator,
            ICodePlotter plotter)
        {
            _streamingHelper = streamingHelper;
            _generator = generator;
            _plotter = plotter;
        }

        [HttpGet("{id}")]
        public Task Image(string id, CancellationToken cancel)
        {
            return _streamingHelper.PullImage(
                id,
                _generator,
                Response,
                cancel);
        }

        [HttpGet("{id}/{layerIndex}")]
        public async Task Image(string id, int layerIndex)
        {
            MimeData image;
            lock (_singleThreadedCreateImageLock)
            {
                image = _plotter.CreateImage(layerIndex: layerIndex);
            }
            Response.ContentType = image.ContentType;
            await Response.Body.WriteAsync(image.Data);
        }
    }
}
