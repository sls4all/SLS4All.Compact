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

namespace SLS4All.Compact.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class PlottedImageController : ControllerBase
    {
        private readonly static object _singleThreadedCreateImageLock = new(); // helps to reduce CPU load when multiple clients are connected and requesting plots
        //private readonly DefaultCalibrationMaskComparer _comparer;
        private readonly ICodePlotter _plotter;

        // TODO: remove comparer
        public PlottedImageController(
            //DefaultCalibrationMaskComparer comparer,
            ICodePlotter plotter)
        {
            //_comparer = comparer;
            _plotter = plotter;
        }

        [HttpGet("{id}")]
        public async Task Image(string id, [FromQuery] double age = 0, [FromQuery] int? maxSize = null)
        {
            MimeData image;
            lock (_singleThreadedCreateImageLock)
            {
                //var mask = Array.Empty<float>();
                //var size = _plotter.GetMask(ref mask);
                //_comparer.FinalizeMask(size.width, size.height, mask);
                //_plotter.ReplaceWith(mask);

                image = _plotter.CreateImage(newerThan: TimeSpan.FromSeconds(age), drawHotspot: true, maxSize: maxSize);
            }
            Response.ContentType = image.ContentType;
            Response.Headers["Cache"] = "no-store, no-cache, must-revalidate";
            await Response.Body.WriteAsync(image.Data);
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
