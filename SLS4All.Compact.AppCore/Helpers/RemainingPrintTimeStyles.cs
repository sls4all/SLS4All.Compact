using System.Runtime.CompilerServices;

namespace SLS4All.Compact.Helpers
{
    public readonly record struct RemainingPrintTimeStyles(string? Visibility, int? Progress, string? Color, double? TransitionTime) : ITuple
    {
        public object? this[int index] => index switch { 0 => Visibility, 1 => Progress, 2 => Color, 3 => TransitionTime, _ => null };
        public int Length => 4;
    }
}
