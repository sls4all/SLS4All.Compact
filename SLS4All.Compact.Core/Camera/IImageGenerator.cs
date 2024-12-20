// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Graphics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public interface IDataGenerator<T>
    {
        bool TryRentLastValue(out T data);
        AsyncEvent<T> Captured { get; }
        IDisposable StartScope();
    }

    public interface IImageGenerator : IDataGenerator<MimeData>
    {
    }
}
