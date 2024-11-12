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
