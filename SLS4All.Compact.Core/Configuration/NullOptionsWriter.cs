// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Options;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Threading;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public sealed class NullOptionsWriter<T> : OptionsWriterBase<T>
    {
        public NullOptionsWriter(IOptionsMonitor<T> options)
            : base(options)
        {
        }

        public override Task Write(T newValue, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public override bool Equals(T x, T y)
        {
            if (ReferenceEquals(x, y))
                return true;
            else
                return PrinterCollectionExtensions.JsonEquals(x, y);
        }
    }
}