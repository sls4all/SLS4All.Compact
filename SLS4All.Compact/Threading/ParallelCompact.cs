using SLS4All.Compact.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public static class ParallelCompact
    {
        /// <summary>
        /// Runs a parallel for loop that enumerates results in order. Each yield will block creation of new tasks. Tasks are created on the caller thread.
        /// </summary>
        public async static IAsyncEnumerable<TResult> OrderedForWithResultBlocking<TNumber, TResult>(
            TNumber from,
            TNumber toExclusive,
            int maxDegreeOfParallelism,
            Func<TNumber, CancellationToken, (TNumber Next, Task<TResult> Task)> factory,
            [EnumeratorCancellation] CancellationToken cancel)
            where TNumber : INumber<TNumber>
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
            var queue = new Queue<Task<TResult>>();
            var running = new List<Task<TResult>>();
            try
            {
                for (var i = from; toExclusive > from ? i < toExclusive : i > toExclusive; )
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();
                        running.Clear();
                        foreach (var task in queue)
                            if (!task.IsCompleted)
                                running.Add(task);
                        if (running.Count < maxDegreeOfParallelism)
                        {
                            var item = factory(i, cancel);
                            queue.Enqueue(item.Task);
                            i = item.Next;
                            break;
                        }
                        else
                        {
                            while (queue.Count > 0)
                            {
                                var first = queue.Peek();
                                if (first.IsCompleted)
                                {
                                    _ = queue.Dequeue();
                                    var result = await first;
                                    yield return result;
                                }
                                else
                                    break;
                            }
                            await Task.WhenAny(running);
                        }
                    }
                }
                while (queue.TryDequeue(out var task))
                {
                    var result = await task;
                    yield return result;
                }
            }
            finally
            {
                try
                {
                    await Task.WhenAll(queue);
                }
                catch
                {
                    // swallow, should have already thrown
                }
            }
        }

        /// <summary>
        /// Runs a parallel for loop that enumerates results in order. Each yield will block creation of new tasks. Tasks are created on the caller thread.
        /// </summary>
        public static IAsyncEnumerable<TResult> OrderedForWithResultNonBlocking<TNumber, TResult>(
            TNumber from,
            TNumber toExclusive,
            int maxDegreeOfParallelism,
            Func<TNumber, CancellationToken, (TNumber Next, Task<TResult> Task)> factory,
            CancellationToken cancel)
            where TNumber : INumber<TNumber>
        {
            var channel = Channel.CreateUnbounded<TResult>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true,
            });
            var task = Task.Run(async () =>
            {
                try
                {
                    await foreach (var item in ParallelCompact.OrderedForWithResultBlocking<TNumber, TResult>(
                        from,
                        toExclusive,
                        maxDegreeOfParallelism,
                        factory,
                        cancel))
                    {
                        await channel.Writer.WriteAsync(item, cancel);
                    }
                    channel.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    channel.Writer.TryComplete(ex);
                }
            });
            return channel.Reader.ReadAllAsync(default /* do not pass cancel, leave the writer task to cancel itself */);
        }
    }
}
