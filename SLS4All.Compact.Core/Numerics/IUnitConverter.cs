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
