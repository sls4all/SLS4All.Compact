// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using MediatR.NotificationPublishers;

namespace SLS4All.Compact.Printing
{
    public record class PrintingEstimate(
        double Progress,
        TimeSpan Elapsed,
        TimeSpan TimeTotal,
        TimeSpan Remaining,
        bool Incomplete,
        int SoftCount = default,
        int LayerPrintDone = default,
        int WeightsDone = default,
        TimeSpan WeightDone = default,
        TimeSpan WeightTotal = default,
        TimeSpan WeightTotalEstimate = default,
        TimeSpan LayerRecoatElapsed = default,
        TimeSpan LayerRecoatDelays = default,
        TimeSpan LayerRecoatPrintLayersElapsed = default,
        TimeSpan LayerRecoatNoDelayAverage = default,
        TimeSpan StreamingElapsed = default,
        TimeSpan ObjectLayerOverheadAverageDuration = default,
        TimeSpan TimeLayers = default,
        TimeSpan CoolingElapsed = default,
        TimeSpan CoolingEstimate = default,
        TimeSpan HeatingElapsed = default,
        TimeSpan HeatingEstimate = default,
        TimeSpan HeatingEstimate2 = default,
        TimeSpan HeatingEstimate3 = default,
        double? CoolingFrom = null)
    {
        public virtual string GetCsvHeader() 
            => $"{nameof(Progress)};{nameof(Elapsed)};{nameof(TimeTotal)};{nameof(Remaining)};{nameof(Incomplete)};{nameof(SoftCount)};{nameof(LayerPrintDone)};{nameof(WeightsDone)};{nameof(WeightDone)};{nameof(WeightTotal)};{nameof(WeightTotalEstimate)};{nameof(LayerRecoatElapsed)};{nameof(LayerRecoatDelays)};{nameof(LayerRecoatPrintLayersElapsed)};{nameof(LayerRecoatNoDelayAverage)};{nameof(StreamingElapsed)};{nameof(ObjectLayerOverheadAverageDuration)};{nameof(TimeLayers)};{nameof(CoolingElapsed)};{nameof(CoolingEstimate)};{nameof(HeatingElapsed)};{nameof(HeatingEstimate)};{nameof(HeatingEstimate2)};{nameof(HeatingEstimate3)};{nameof(CoolingFrom)}";
        public virtual string ToCsv()
            => $"{Progress};{Elapsed};{TimeTotal};{Remaining};{Incomplete};{SoftCount};{LayerPrintDone};{WeightsDone};{WeightDone};{WeightTotal};{WeightTotalEstimate};{LayerRecoatElapsed};{LayerRecoatDelays};{LayerRecoatPrintLayersElapsed};{LayerRecoatNoDelayAverage};{StreamingElapsed};{ObjectLayerOverheadAverageDuration};{TimeLayers};{CoolingElapsed};{CoolingEstimate};{HeatingElapsed};{HeatingEstimate};{HeatingEstimate2};{HeatingEstimate3};{CoolingFrom}";
    }
}