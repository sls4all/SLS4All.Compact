// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Numerics
{
    public readonly record struct UnitValue(decimal Value, string Unit);

    [Flags]
    public enum UnitConverterFlags
    {
        None = 0,
        PreferCelsius = 1,
        PreferFahrenheit = 2,
        PreferMetric = 4,
        PreferImperial = 8,
    }

    public interface IUnitConverter
    {
        UnitValue[] TryGetAlternateUnits(decimal value, string unit);
        UnitValue GetUnits(decimal value, string unit, UnitConverterFlags flags);
    }

    public static class UnitConverterExtensions
    {
        public static UnitValue GetUnits(this IUnitConverter unitConverter, float value, string unit, UnitConverterFlags flags)
            => unitConverter.GetUnits((decimal)value, unit, flags);
        public static UnitValue GetUnits(this IUnitConverter unitConverter, double value, string unit, UnitConverterFlags flags)
            => unitConverter.GetUnits((decimal)value, unit, flags);
    }
}
