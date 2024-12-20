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
using SLS4All.Compact.Pages;
using Microsoft.AspNetCore.StaticFiles;

namespace SLS4All.Compact.Controllers
{
    [Route(PrintTuningReportPage.SelfPath)]
    [ApiController]
    [Authorize]
    public class PrintTuningReportPageController : ControllerBase
    {
        private static readonly char[] _prohibitedChars = ['/', '\\', ':'];
        private readonly IPrintAutoTuner _autoTuner;
        private readonly IContentTypeProvider _contentTypeProvider;

        public PrintTuningReportPageController(
            IPrintAutoTuner autoTuner,
            IContentTypeProvider contentTypeProvider)
        {
            _autoTuner = autoTuner;
            _contentTypeProvider = contentTypeProvider;
        }


        [HttpGet("{filename}")]
        public object ReportFile(string filename, CancellationToken cancel)
        {
            var dir = _autoTuner.ReportDirectory;
            if (string.IsNullOrEmpty(dir) || !Path.Exists(dir) || filename.IndexOfAny(_prohibitedChars) != -1)
                return NotFound();
            var path = Path.Combine(dir, filename);
            if (!_contentTypeProvider.TryGetContentType(path, out var contentType))
                contentType = "application/octet-stream";
            try
            {
                var stream = System.IO.File.OpenRead(path);
                return File(stream, contentType);
            }
            catch (IOException)
            {
                return NotFound();
            }
        }
    }
}
