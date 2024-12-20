// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Azure;
using Lexical.FileProvider.Package;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public abstract class StyleStreamingHelperBase<T> : StreamingHelperBase<T>
        where T: struct, ITuple
    {
        public StyleStreamingHelperBase(
            ILogger<ImageStreamingHelper> logger,
            IOptionsMonitor<StreamingHelperOptions> options,
            T placeholderValue)
            : base(logger, options, placeholderValue)
        {
        }

        public sealed override ValueTask Write(T data, HttpResponse response, CancellationToken cancel)
            => throw new NotSupportedException();

        protected sealed override T RentCopy(T data)
            => data;

        protected sealed override void Return(T data)
        {
        }

        protected sealed override bool IsEmpty(T data)
            => data.Equals(new T());
    }
}
