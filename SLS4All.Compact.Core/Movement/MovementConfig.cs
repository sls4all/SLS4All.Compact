// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Printer;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Movement
{
    public class MovementConfigOptions
    {
        public double MaxXY { get; set; } = 1000;
        public double StepXYDistance { get; set; } = 0.0152590218966964;

        public MovementConfigOptions Clone()
            => (MovementConfigOptions)MemberwiseClone();

        public virtual void CopyFrom(MovementConfigOptions config)
        {
            MaxXY = config.MaxXY;
            StepXYDistance = config.StepXYDistance;
        }
    }

    public sealed class MovementConfig : IMovementConfig
    {
        private readonly IOptionsMonitor<MovementConfigOptions> _options;

        public double MaxXY => _options.CurrentValue.MaxXY;
        public double StepXYDistance => _options.CurrentValue.StepXYDistance;

        public MovementConfig(
            IOptionsMonitor<MovementConfigOptions> options)
        {
            _options = options;
        }

        public (int x, int y) XYToSteps(double x, double y, double stepXYDistance = 0)
        {
            if (stepXYDistance == 0)
                stepXYDistance = _options.CurrentValue.StepXYDistance;
            return (
                (int)Math.Round(x / stepXYDistance),
                (int)Math.Round(y / stepXYDistance));
        }

        public (double x, double y) StepsToXY(int x, int y, double stepXYDistance = 0)
        {
            if (stepXYDistance == 0)
                stepXYDistance = _options.CurrentValue.StepXYDistance;
            return (
                x * stepXYDistance,
                y * stepXYDistance);
        }

        public bool IsMoveXYCode(in CodeCommand cmd, out double x, out double y, out bool relative)
        {
            x = 0;
            y = 0;
            relative = false;
            return false;
        }

        public bool IsSetLaserCode(in CodeCommand cmd, out double value, out bool noCompensation)
        {
            value = 0;
            noCompensation = false;
            return false;
        }
    }
}
