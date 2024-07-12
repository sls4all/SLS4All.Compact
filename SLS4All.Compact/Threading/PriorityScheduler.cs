// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public sealed class PriorityScheduler : TaskScheduler, IDisposable
    {
        /// <summary>
        /// Queues tasks to BelowNormal scheduler
        /// </summary>
        public static TaskScheduler LowerBackgroundTaskPriority = new PriorityScheduler("LowerBackground", ThreadPriority.BelowNormal, null);

        private const int PRIO_PROCESS = 0;
        private readonly int _maximumConcurrencyLevel;
        private readonly string _nameFormat;
        private readonly BlockingCollection<StrongBox<Task?>> _tasks = new();
        private readonly ThreadPriority _priority;
        private readonly bool _isBackground;
        private Thread[]? _threads;

        public PriorityScheduler(string nameFormat, ThreadPriority priority, int? maximumConcurrency, bool isBackground = true)
        {
            _nameFormat = nameFormat;
            _priority = priority;
            _isBackground = isBackground;
            _maximumConcurrencyLevel = maximumConcurrency ?? Math.Max(Environment.ProcessorCount * 2, 10);
        }

        public ThreadPriority Priority => _priority;

        public override int MaximumConcurrencyLevel
        {
            get { return _maximumConcurrencyLevel; }
        }

        protected override IEnumerable<Task> GetScheduledTasks()
            => _tasks.Select(x => x.Value).Where(x => x != null).ToArray()!;
        
        protected override void QueueTask(Task task)
        {
            _tasks.Add(new StrongBox<Task?>(task));
            if (_threads == null)
            {
                lock (_tasks)
                {
                    if (_threads == null)
                    {
                        _threads = new Thread[_maximumConcurrencyLevel];
                        for (int i = 0; i < _threads.Length; i++)
                        {
                            _threads[i] = new Thread(() =>
                            {
                                SetCurrentThreadPriority(_priority);
                                foreach (var task in _tasks.GetConsumingEnumerable())
                                {
                                    var innerTask = task.Value;
                                    if (innerTask != null)
                                    {
                                        // NOTE: free for GC (important!)
                                        //       otherwise it might be kept referenced internally by the BlockingCollection
                                        task.Value = null; 
                                        TryExecuteTask(innerTask);
                                    }
                                }
                            });
                            _threads[i].Name = string.Format(_nameFormat, i, _priority);
                            _threads[i].Priority = _priority;
                            _threads[i].IsBackground = _isBackground;
                            _threads[i].Start();
                        }
                    }
                }
            }
        }

        protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
        {
            if (Thread.CurrentThread.Priority == _priority)
                return TryExecuteTask(task);
            else
                return false;
        }

        [DllImport("libc")]
        private static extern int setpriority(int which, int who, int prio);
        [DllImport("libc")]
        private static extern int gettid();

        public static void SetCurrentThreadPriority(ThreadPriority priority)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var tid = GetCurrentTid();
                SetThreadPriority(tid, priority);
            }
        }

        public static int GetCurrentTid()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return gettid();
            else
                return -1;
        }

        public static void SetThreadPriority(int tid, ThreadPriority priority)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var nice = priority switch
                {
                    ThreadPriority.Highest => -20,
                    ThreadPriority.AboveNormal => -10,
                    ThreadPriority.Normal => 0,
                    ThreadPriority.BelowNormal => 10,
                    ThreadPriority.Lowest => 20,
                    _ => throw new ArgumentOutOfRangeException(nameof(priority)),
                };
                setpriority(PRIO_PROCESS, tid, nice);
            }
        }

        public void Dispose()
        {
            _tasks.CompleteAdding();
            // NOTE: threads will end on their own due to completed blocking collection
        }
    }
}
