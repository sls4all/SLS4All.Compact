// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
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
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Printing;

namespace SLS4All.Compact.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SlicingImageController : ControllerBase
    {
        private readonly IPrintingService _printing;
        private readonly ILogger<PlottedImageController> _logger;

        public SlicingImageController(
            IPrintingService printing,
            ILogger<PlottedImageController> logger)
        {
            _printing = printing;
            _logger = logger;
        }

        [HttpGet("{version}/{layerIndex}")]
        public async Task Image(string version, int layerIndex)
        {
            var image = _printing.TryGetPreviewLayer(layerIndex);
            var data = image?.Plot ?? image?.Preview ?? MimeData.BlackPng;
            Response.ContentType = data.ContentType;
            await Response.Body.WriteAsync(data.Data);
        }
    }
}
