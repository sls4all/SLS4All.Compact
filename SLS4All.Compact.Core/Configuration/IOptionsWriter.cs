// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Options;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public interface IOptionsWriter<T>
    {
        T CurrentValue { get; }
        bool Equals(T x, T y);
        IDisposable? OnChange(Action<T> listener);
        Task Write(T newValue, CancellationToken cancel);
    }
}