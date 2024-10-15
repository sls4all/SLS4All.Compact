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
    [Route("api/[controller]")]
    [ApiController]
    public class BedMatrix2Controller : BedMatrixController
    {
        public BedMatrix2Controller(
            ITemperatureCamera2 camera,
            ILogger<BedMatrix2Controller> logger,
            IOptionsMonitor<BedMatrixControllerOptions> options,
            ImageStreamingHelper streamingHelper)
            : base(camera, logger, options, streamingHelper)
        {
        }
    }
}
