// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.IO;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
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
            int maxQueuedResults,
            Func<TNumber, CancellationToken, (TNumber Next, Task<TResult> Task)> factory,
            [EnumeratorCancellation] CancellationToken cancel)
            where TNumber : INumber<TNumber>
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
            var queue = new Queue<Task<TResult>>();
            var running = new List<Task>();
            var done = new List<Task>();
            try
            {
                for (var i = from; toExclusive > from ? i < toExclusive : i > toExclusive; )
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();
                        running.Clear();
                        done.Clear();
                        // NOTE: we need to collect done tasks and tasks to wait for in single step, otherwise we risk starting more tasks than requested by degreeOfParalleism
                        var taskIndex = 0;
                        foreach (var task in queue)
                        {
                            if (task.IsCompleted)
                            {
                                if (done.Count == taskIndex) // add only first uninterupted sequence of tasks to preserve order
                                    done.Add(task);
                            }
                            else
                                running.Add(task);
                            taskIndex++;
                        }
                        foreach (var task in done)
                        {
                            var dequeued = queue.Dequeue();
                            Debug.Assert(ReferenceEquals(dequeued, task));
                            var result = await dequeued;
                            yield return result;
                        }
                        if (running.Count < maxDegreeOfParallelism && queue.Count < maxQueuedResults)
                        {
                            var item = factory(i, cancel);
                            queue.Enqueue(item.Task);
                            i = item.Next;
                            break;
                        }
                        else
                        {
                            Debug.Assert(running.Count > 0);
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
        /// Runs a parallel for loop that enumerates results in order. Each yield will NOT block creation of new tasks. Tasks are created on the caller thread.
        /// </summary>
        public static IAsyncEnumerable<TResult> OrderedForWithResultNonBlocking<TNumber, TResult>(
            TNumber from,
            TNumber toExclusive,
            int maxDegreeOfParallelism,
            int maxQueuedResults,
            Func<TNumber, CancellationToken, (TNumber Next, Task<TResult> Task)> factory,
            CancellationToken cancel)
            where TNumber : INumber<TNumber>
        {
            var channel = Channel.CreateBounded<TResult>(new BoundedChannelOptions(maxQueuedResults)
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
                        maxQueuedResults,
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

        /// <summary>
        /// Runs a parallel for loop that enumerates results in order. Each yield will block creation of new tasks. Tasks are created on the caller thread.
        /// </summary>
        public async static IAsyncEnumerable<TResult> OrderedForWithResultBlocking<TInput, TResult>(
            IAsyncEnumerable<TInput> source,
            int maxDegreeOfParallelism,
            int maxQueuedResults,
            Func<TInput, CancellationToken, Task<TResult>> factory,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDegreeOfParallelism);
            var queue = new Queue<Task<TResult>>();
            var running = new List<Task>();
            var done = new List<Task>();
            try
            {
                await foreach (var input in source.WithCancellation(cancel))
                {
                    while (true)
                    {
                        cancel.ThrowIfCancellationRequested();
                        running.Clear();
                        done.Clear();
                        // NOTE: we need to collect done tasks and tasks to wait for in single step, otherwise we risk starting more tasks than requested by degreeOfParalleism
                        var taskIndex = 0;
                        foreach (var task in queue)
                        {
                            if (task.IsCompleted)
                            {
                                if (done.Count == taskIndex) // add only first uninterupted sequence of tasks to preserve order
                                    done.Add(task);
                            }
                            else
                                running.Add(task);
                            taskIndex++;
                        }
                        foreach (var task in done)
                        {
                            var dequeued = queue.Dequeue();
                            Debug.Assert(ReferenceEquals(dequeued, task));
                            var result = await dequeued;
                            yield return result;
                        }
                        if (running.Count < maxDegreeOfParallelism && queue.Count < maxQueuedResults)
                        {
                            var item = factory(input, cancel);
                            queue.Enqueue(item);
                            break;
                        }
                        else
                        {
                            Debug.Assert(running.Count > 0);
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
        /// Runs a parallel for loop that enumerates results in order. Each yield will NOT block creation of new tasks. Tasks are created on the caller thread.
        /// </summary>
        public static async IAsyncEnumerable<TResult> OrderedForWithResultNonBlocking<TInput, TResult>(
            IAsyncEnumerable<TInput> source,
            int maxDegreeOfParallelism,
            int maxQueuedResults,
            Func<TInput, CancellationToken, Task<TResult>> factory,
            [EnumeratorCancellation] CancellationToken cancel)
        {
            var channel = Channel.CreateUnbounded<TResult>(new UnboundedChannelOptions
            {
                AllowSynchronousContinuations = false,
                SingleReader = true,
                SingleWriter = true,
            });
            using var cancelSource = CancellationTokenSource.CreateLinkedTokenSource(cancel);
            try
            {
                var innerCancel = cancelSource.Token;
                using var semaphoreSlim = new SemaphoreSlim(maxQueuedResults, maxQueuedResults);
                var task = Task.Run(async () =>
                {
                    try
                    {
                        await foreach (var item in ParallelCompact.OrderedForWithResultBlocking<TInput, TResult>(
                            source,
                            maxDegreeOfParallelism,
                            maxQueuedResults,
                            factory,
                            innerCancel))
                        {
                            await semaphoreSlim.WaitAsync(innerCancel);
                            await channel.Writer.WriteAsync(item, innerCancel);
                        }
                        channel.Writer.TryComplete();
                    }
                    catch (Exception ex)
                    {
                        channel.Writer.TryComplete(ex);
                    }
                });
                await foreach (var result in channel.Reader.ReadAllAsync(default /* do not pass cancel, leave the writer task to cancel itself */))
                {
                    yield return result;
                    semaphoreSlim.Release();
                }
            }
            finally
            {
                // in case the caller stops reading
                cancelSource.Cancel();
            }
        }
    }
}
