// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Collections;

namespace SLS4All.Compact.Printing
{
    public class PrintingServiceLayerStats
    {
        public ILayerEstimateExtrapolator? LayerEstimateExtrapolator { get; set; }
        public TimeSpan? RecoatOnlyAverageDuration { get; set; }
        public TimeSpan? ObjectLayerOverheadDuration { get; set; }
        public TimeSpan TotalDuration { get; set; }
        public TimeSpan HeatingDuration { get; set; }
        public TimeSpan ObjectsDuration { get; set; }
        public TimeSpan CoolingDuration { get; set; }
        public bool PrintDurationIncomplete { get; set; }
    }
}