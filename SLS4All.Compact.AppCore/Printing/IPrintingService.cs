// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Collections;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.PrintSessions;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Storage;
using SLS4All.Compact.Storage.PrinterSettings;
using SLS4All.Compact.Storage.PrintJobs;
using SLS4All.Compact.Storage.PrintProfiles;
using SLS4All.Compact.Storage.PrintSessions;
using SLS4All.Compact.Threading;
using System.Collections.Concurrent;

namespace SLS4All.Compact.Printing
{
    public record class PrintingStatus(PrintingPhase Phase, double PhaseDone, double PhaseTotal, PrintingEstimate Estimate, string JobName);

    public enum PrintingSoftCancelMode
    {
        NotSet = 0,
        CapAndCool,
        CoolNow,
    }

    public record class LastStreamingLayerInfo(int Index, int Count);

    public enum PrintingMode
    {
        NotSet = 0,
        Other,
        PrintLayers,
        AnalyseHeating,
        PowderTuning,
    }

    public class PowderTuningSetParametersCommand
    {
        public int? FillPhase { get; set; }
        public int? GridIndex { get; set; }
        public int? PrintLabelIndex { get; set; }
    }

    public class PowderTuningPrintCommand
    {
        public PrintSetup? PrintSetupSource { get; set; }
        public PrintSetup? PrintSetupResult { get; set; }
        public Func<PrintSetup, CancellationToken, ValueTask<PrintSetup?>> SetupFunc { get; set; } 
            = (x, y) => ValueTask.FromResult<PrintSetup?>(null);
        public bool PrintNumberEnabled { get; set; }
    }

    public class PowderTuningSetSurfaceCommand
    {
        public double? SurfaceTemperature { get; set; }
    }

    public interface IPrintingService
    {
        public static WeakConcurrentDictionary<string, IPrintingService> Services { get; } = new();
        string Id { get; }
        BackgroundTask<PrintingStatus> BackgroundTask { get; }
        long PreviewVersion { get; }
        LastStreamingLayerInfo? LastStreamingLayer { get; }
        bool IsPrinting { get; }
        PrintingMode PrintingMode { get; }
        PrintingPhase PrintingPhase { get; }
        int PreviewLayerFinalCount { get; }
        PrintedLayer[] PreviewLayers { get; }
        PrintingSoftCancelMode SoftCancelMode { get; }
        PrintingSoftCancelMode[] SoftCancelAllowedModes { get; }
        PrintSetupOverrides SetupOverrides { get; set; }
        PrintSetupOverrides SetupOverridesDefaults { get; }

        void Clear();
        Task PowderTuning(PrintSetup setup, string jobName, CancellationToken cancel);
        Task<object?> ExecutePowderTuningCommand(object command, StatusUpdater? statusUpdater, CancellationToken cancel);
        Task AnalyseHeating(PrintSetup setup, string jobName, CancellationToken cancel);
        Task<PrintSetup> CreateSetup(IPrintJob? job, PrintProfile profile, PrinterPowerSettings powerSettings);
        PrintingServiceLayerStats GetLayerStats(PrintingParameters parameters);
        Task PlotLayer(
            INestingService? nesting, 
            IReadOnlyList<PrintingObject>? instancesOverride, 
            PrintedLayer layer, 
            PrintSetup setup, 
            CancellationToken cancel = default);
        Task PrintLayers(
            INestingService? nesting, 
            IReadOnlyList<PrintingObject>? instancesOverride,
            LayerWeight[]? previewOverride,
            PrintSetup setup, 
            string jobName,
            PrintingServiceLayerStats? stats,
            Func<CancellationToken, Task>? cleanupBeforePrint,
            Func<PrintingServiceLayerStats, Task>? saveStats,
            CancellationToken hardCancel);
        Task<LayerWeight[]> ProcessPreviews(
            INestingService? nesting, 
            IReadOnlyList<PrintingObject>? instancesOverride, 
            PrintSetup setup, 
            bool refreshOnly, 
            PrintingServiceLayerStats? stats,
            Func<PrintingServiceLayerStats, Task>? saveStats,
            CancellationToken cancel);
        void UpdateLayerStatsProfile(PrintingServiceLayerStats stats, PrintProfile profile);
        void SoftCancel(PrintingSoftCancelMode mode);
    }

    public static class PrintingServiceExtensions
    {
        public static PrintedLayer? TryGetPreviewLayer(this IPrintingService service, int index)
        {
            var layers = service.PreviewLayers;
            if (layers.Length == 0)
                return null;
            if (index < 0)
                index = 0;
            else if (index >= layers.Length)
                index = layers.Length - 1;
            return layers[index];
        }
    }

    public interface IPrintingServiceScoped : IPrintingService
    {
    }
}