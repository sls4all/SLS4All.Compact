// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    /// <summary>
    /// Chains tasks to be executed asynchronously in a sequence
    /// </summary>
    public sealed class TaskQueue
    {
        private readonly Queue<(Delegate Delegate, object? State)> _queue = new();
        private Task _currentTask = Task.CompletedTask;
        private bool _isRunning;

        /// <summary>
        /// Gets task that completes when all current and possibly subsequent items have been completed
        /// </summary>
        public Task CurrentTask => _currentTask;

        /// <summary>
        /// Queues new task on thread pool. Throws exception if preceding task failed. Does not wait or block.
        /// </summary>
        public void Enqueue(Func<Task> taskFactory, ILogger? logger, bool doThrow = false)
            => EnqueueInner(taskFactory, logger, doThrow, null);

        /// <summary>
        /// Queues new task on thread pool. Throws exception if preceding task failed. Does not wait or block.
        /// </summary>
        public void EnqueueValue(Func<ValueTask> taskFactory, ILogger? logger, bool doThrow = false)
            => EnqueueInner(taskFactory, logger, doThrow, null);

        /// <summary>
        /// Queues new task on thread pool. Throws exception if preceding task failed. Does not wait or block.
        /// </summary>
        public void Enqueue(Func<object?, Task> taskFactory, object? state, ILogger ?logger, bool doThrow = false)
            => EnqueueInner(taskFactory, logger, doThrow, state);

        /// <summary>
        /// Queues new task on thread pool. Throws exception if preceding task failed. Does not wait or block.
        /// </summary>
        public void EnqueueValue(Func<object?, ValueTask> taskFactory, object? state, ILogger ? logger, bool doThrow = false)
            => EnqueueInner(taskFactory, logger, doThrow, state);

        /// <summary>
        /// Queues new task on thread pool. Throws exception if preceding task failed. Does not wait or block.
        /// </summary>
        private void EnqueueInner(Delegate taskFactory, ILogger? logger, bool doThrow, object? state)
        {
            try
            {
                Task task;
                lock (_queue)
                {
                    _queue.Enqueue((taskFactory, state));
                    if (_isRunning)
                        return;
                    _isRunning = true;
                    task = _currentTask;
                    _currentTask = Task.Run(ProcessItems);
                }
                task.GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                logger?.LogError(ex, $"Exception while processing task chain");
                if (doThrow)
                    throw;
            }
        }

        private async Task ProcessItems()
        {
            while (true)
            {
                (Delegate Delegate, object? State) item;
                lock (_queue)
                {
                    Debug.Assert(_isRunning);
                    if (!_queue.TryDequeue(out item!))
                    {
                        _isRunning = false;
                        break;
                    }
                }
                try
                {
                    if (item.Delegate is Func<ValueTask> valueTaskFunc)
                        await valueTaskFunc();
                    else if (item.Delegate is Func<Task> taskFunc)
                        await taskFunc();
                    else if (item.Delegate is Func<object?, ValueTask> valueTaskFunc2)
                        await valueTaskFunc2(item.State);
                    else if (item.Delegate is Func<object?, Task> taskFunc2)
                        await taskFunc2(item.State);
                }
                catch
                {
                    lock (_queue)
                    {
                        _isRunning = false;
                    }
                    throw;
                }
            }
        }
    }
}
