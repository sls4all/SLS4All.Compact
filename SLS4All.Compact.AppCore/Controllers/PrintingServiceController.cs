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
using System.Diagnostics;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Printing;
using Microsoft.AspNetCore.Authorization;
using SLS4All.Compact.Temperature;

namespace SLS4All.Compact.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class PrintingServiceController : ControllerBase
    {
        private readonly IPrintingService _printing;
        private readonly IPrinterPerformanceProvider _performance;
        private readonly ISurfaceHeater _surfaceHeater;
        private readonly ILogger<PrintingServiceController> _logger;

        public PrintingServiceController(
            IPrintingService printing,
            IPrinterPerformanceProvider performance,
            ISurfaceHeater surfaceHeater,
            ILogger<PrintingServiceController> logger)
        {
            _printing = printing;
            _performance = performance;
            _surfaceHeater = surfaceHeater;
            _logger = logger;
        }

        [HttpGet("status")]
        public async Task<object?> Status()
        {
            await Task.CompletedTask;
            var status = _printing.BackgroundTask.Status;
            var performance = _performance.Values;
            const long MB = 1024 * 1024;
            var surfaceTarget = _surfaceHeater.TargetTemperature;
            return new
            {
                SurfaceTarget = surfaceTarget != null ? (double?)Math.Round(surfaceTarget.Value, 2) : null,
                Progress = MathF.Round(status?.Progress ?? 0, 1),
                Remaining = status?.ProgressStatus?.Estimate.Remaining ?? TimeSpan.Zero,
                RemainingIncomplete = status?.ProgressStatus?.Estimate.Incomplete ?? true,
                Phase = status?.ProgressStatus?.Phase,
                PhaseDone = status?.ProgressStatus?.PhaseDone,
                PhaseTotal = status?.ProgressStatus?.PhaseTotal,
                SelfCpuLoad = MathF.Round(performance.SelfCpuLoad, 1),
                TotalCpuLoad = MathF.Round(performance.TotalCpuLoad, 1),
                TotalIOLoad = MathF.Round(performance.TotalIOLoad, 1),
                SelfUsedMemory = performance.SelfUsedMemory / MB,
                TotalUsedMemory = performance.TotalUsedMemory / MB,
                TotalAvaialableMemory = performance.TotalAvaialableMemory / MB,
                CpuTemp = performance.CpuTemperature != null ? (float?)MathF.Round(performance.CpuTemperature.Value, 1) : null,
                GpuTemp = performance.GpuTemperature != null ? (float?)MathF.Round(performance.GpuTemperature.Value, 1) : null,
                JobName = status?.ProgressStatus?.JobName,
            };
        }
    }
}
