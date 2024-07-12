// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Threading.Channels;

namespace SLS4All.Compact.Printer
{
    public static class PrinterClientExtensions
    {
        public static async ValueTask Consume(this PrinterStream script, CancellationToken cancel = default)
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

        public static async ValueTask Consume(this PrinterStream script, Func<CodeCommand, CancellationToken, ValueTask> execute, CancellationToken cancel = default)
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

        public static async ValueTask<T?> Consume<T>(this PrinterStream script, Func<CodeCommand, CancellationToken, ValueTask<T>> execute, CancellationToken cancel = default)
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
