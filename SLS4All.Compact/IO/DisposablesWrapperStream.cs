// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public sealed class DisposablesWrapperStream : WrapperStreamBase
    {
        private readonly List<IDisposable> _disposables = new();

        public List<IDisposable> Disposables => _disposables;

        public DisposablesWrapperStream(Stream stream) : base(stream)
        {
        }

        public override void Close()
        {
            try
            {
                base.Close();
            }
            finally
            {
                foreach (var disposable in _disposables)
                    disposable.Dispose();
            }
        }

        protected override void Dispose(bool disposing)
        {
            try
            {
                base.Dispose(disposing);
            }
            finally
            {
                foreach (var disposable in _disposables)
                    disposable.Dispose();
            }
        }
    }
}
