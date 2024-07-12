// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public delegate ValueTask PrinterStream(ChannelWriter<CodeCommand> channel, CancellationToken cancel);

    public static class PrinterStreamExtensions
    {
        private sealed class ListWriter : ChannelWriter<CodeCommand>
        {
            private readonly List<CodeCommand> _list;
            private Exception? _exception;

            public Exception? Exception => _exception;

            public ListWriter(List<CodeCommand> list)
            {
                _list = list;
            }

            public override bool TryWrite(CodeCommand item)
            {
                _list.Add(item);
                return true;
            }

            public override ValueTask<bool> WaitToWriteAsync(CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return ValueTask.FromResult(true);
            }

            public override ValueTask WriteAsync(CodeCommand item, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _list.Add(item);
                return ValueTask.CompletedTask;
            }

            public override bool TryComplete(Exception? error = null)
            {
                _exception = error;
                return true;
            }
        }

        public static void Flatten(this PrinterStream script, List<CodeCommand> list, CancellationToken cancel)
        {
            var writer = new ListWriter(list);
            script(writer, cancel);
        }

        public static CodeCommand[] Flatten(this PrinterStream script, CancellationToken cancel)
        {
            var list = new List<CodeCommand>();
            Flatten(script, list, cancel);
            return list.ToArray();
        }
    }
}
