// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.


using System.Runtime.CompilerServices;

namespace SLS4All.Compact.Temperature
{
    public record class PrintAutoTunerStartArgs
    {
        public string? DismissWarningUri { get; set; }
        public bool? AutoDetectEnabled { get; set; }
        public double? FinalTemperatureOffset { get; set; }
        public double? StartTemperature { get; set; }
        public double? MaxAutoTemperature { get; set; }
        public double? AutoDetectThresholdFactor { get; set; }
        public int LayerCountToStart { get; set; } = 0;
    }

    public interface IPrintAutoTuner
    {
        bool IsRunning { get; }
        Task TuningTask { get; }
        bool AutoDetectEnabled { get; set; }
        double FinalTemperatureOffset { get; set; }
        double AutoDetectThresholdFactor { get; set; }
        int LayerCountToStartRemaining { get; }
        string? ReportDirectory { get; }
        string ReportMarkdown { get; }

        Task ChangesDetected(CancellationToken cancel = default);
        void DismissedWarning();
        Task Start(PrintAutoTunerStartArgs args, CancellationToken cancel = default);
        Task Stop();
        int EstimateNumberOfLayers(double startTemperature, double finalTemperature);
    }
}