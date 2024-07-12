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

namespace SLS4All.Compact.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class VideoCameraController : ControllerBase
    {
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
            return _streamingHelper.PullImage(
                id,
                _client.ImageCaptured,
                Response,
                cancel);
        }
    }
}
