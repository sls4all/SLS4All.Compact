using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public sealed record class CollapsedStringWithTraits(string Str)
    {
        public static CollapsedStringWithTraits? Create(string? str)
            => str != null ? new CollapsedStringWithTraits(str) : null;

        public static IInputValueTraits Traits = new DelegatedInputValueTraits(
            typeof(CollapsedStringWithTraits),
            obj =>
            {
                var value = (CollapsedStringWithTraits?)obj;
                if (value == null)
                    return null;
                else
                    return "Click for detail";
            },
            str => Create(str),
            obj => ((CollapsedStringWithTraits?)obj)?.Str);
    }
}
