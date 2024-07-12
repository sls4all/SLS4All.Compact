// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Collections;

namespace SLS4All.Compact.Printing
{
    public enum LayerEstimateExtrapolatorValueSource
    {
        NotSet = 0,
        ProcessedEstimate,
        Real,
    }

    public interface ILayerEstimateExtrapolator
    {
        bool HasData { get; }

        byte[] Serialize();
        bool TryDeserialize(ArraySegment<byte> data);
        void Add(
            double key, 
            double value,
            LayerEstimateExtrapolatorValueSource valueSource);
        double? TryGetValue(double key);
        ILayerEstimateExtrapolator CloneForAdding();
        void ReduceCount(int layerLatencyExtrapolationMaxCount);
    }
}