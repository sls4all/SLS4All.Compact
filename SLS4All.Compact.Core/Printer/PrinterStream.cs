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
using static System.Collections.Specialized.BitVector32;

namespace SLS4All.Compact.Printer
{
    public delegate ValueTask PrinterStream(ChannelWriter<CodeCommand> channel, CancellationToken cancel);

    public static class PrinterStreamExtensions
    {
        private sealed class ListWriter : ChannelWriter<CodeCommand>
        {
            private readonly ICollection<CodeCommand> _list;
            private Exception? _exception;

            public Exception? Exception => _exception;

            public ListWriter(ICollection<CodeCommand> list)
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

        public static void Flatten(this PrinterStream script, ICollection<CodeCommand> list, CancellationToken cancel = default)
        {
            var writer = new ListWriter(list);
            script(writer, cancel);
        }

        public static CodeCommand[] Flatten(this PrinterStream script, CancellationToken cancel = default)
        {
            var list = new List<CodeCommand>();
            Flatten(script, list, cancel);
            return list.ToArray();
        }

        public static async ValueTask Execute(this PrinterStream script, CancellationToken cancel = default)
        {
            var channel = Channel.CreateBounded<CodeCommand>(new BoundedChannelOptions(1000)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = true,
            });
            await Task.Run(async () =>
            {
                try
                {
                    await script(channel.Writer, cancel);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            });
            await foreach (var command in channel.Reader.ReadAllAsync(cancel))
            {
            }
        }

        public static async ValueTask Execute(this PrinterStream script, Func<CodeCommand, CancellationToken, ValueTask> execute, CancellationToken cancel = default)
        {
            var channel = Channel.CreateBounded<CodeCommand>(new BoundedChannelOptions(1000)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
            var task = Task.Run(async () =>
            {
                try
                {
                    await script(channel.Writer, cancel);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            });
            await foreach (var command in channel.Reader.ReadAllAsync(cancel))
            {
                await execute(command, cancel);
            }
        }

        public static async ValueTask<T?> Execute<T>(this PrinterStream script, Func<CodeCommand, CancellationToken, ValueTask<T>> execute, CancellationToken cancel = default)
        {
            var channel = Channel.CreateBounded<CodeCommand>(new BoundedChannelOptions(1000)
            {
                AllowSynchronousContinuations = false,
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = true,
            });
            var task = Task.Run(async () =>
            {
                try
                {
                    await script(channel.Writer, cancel);
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            });
            T? result = default;
            await foreach (var command in channel.Reader.ReadAllAsync(cancel))
            {
                result = await execute(command, cancel);
            }
            return result;
        }
    }
}
