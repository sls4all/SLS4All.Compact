// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Diagnostics;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    /// <summary>
    /// Immutable status of <see cref="CollapseTask"/> task. (Immutable except of <see cref="Elapsed"/> value)
    /// </summary>
    public sealed class BackgroundTaskStatus<TStatus>
        where TStatus: class
    {
        private readonly SystemTimestamp _createdAt;
        private readonly long? _startedTimestamp;
        private readonly TimeSpan _elapsed;
        private readonly float _progress;
        private readonly TimeSpan? _progressEstimate;
        private readonly TStatus? _progressStatus;
        private readonly Exception? _exception;

        /// <summary>
        /// Gets the system time the status was created
        /// </summary>
        public SystemTimestamp CreatedAt => _createdAt;
        /// <summary>
        /// Gets how much time has elapsed since task started. Stops increasing when the task completes.
        /// </summary>
        public TimeSpan Elapsed
        {
            get
            {
                if (_startedTimestamp != null)
                    return Stopwatch.GetElapsedTime(_startedTimestamp.Value);
                else
                    return _elapsed;
            }
        }
        /// <summary>
        /// Gets the progress indicated by <see cref="BackgroundTask.UpdateProgress"/>
        /// </summary>
        public float Progress => _progress;
        /// <summary>
        /// Gets optional object associated with the status
        /// </summary>
        public TimeSpan? ProgressEstimate => _progressEstimate;
        /// <summary>
        /// Gets optional object associated with the status
        /// </summary>
        public TStatus? ProgressStatus => _progressStatus;
        /// <summary>
        /// Gets any exception that completed the task
        /// </summary>
        public Exception? Exception => _exception;
        /// <summary>
        /// Gets whether has the task completed
        /// </summary>
        public bool IsCompleted => _startedTimestamp == null;

        public BackgroundTaskStatus(long? stopwatch, TimeSpan elapsed, float progress, TimeSpan? estimate, TStatus? progressStatus, Exception? exception)
        {
            _createdAt = SystemTimestamp.Now;
            _startedTimestamp = stopwatch;
            _elapsed = elapsed;
            _progress = progress;
            _progressEstimate = estimate;
            _progressStatus = progressStatus;
            _exception = exception;
        }

        internal BackgroundTaskStatus<TStatus> CloneWithProgress(float progress, TimeSpan? estimate, TStatus? progressStatus)
            => new BackgroundTaskStatus<TStatus>(_startedTimestamp, _elapsed, progress, estimate, progressStatus, null);
    }

    /// <summary>
    /// Ensures there at most two subsequent background tasks of same type actively running
    /// </summary>
    public class BackgroundTask<TStatus> : IStatusUpdater<TStatus>
        where TStatus : class
    {
        private readonly object _syncRoot = new();
        private readonly List<(Delegate factory, Delegate? destroy, object? type)> _collapseTasks = new();
        private readonly Func<CancellationToken, ValueTask> _stateChangedInvoke;
        private readonly bool _isInternal;
        private BackgroundTask<object>? _eventTaskLazy;
        private CancellationTokenSource? _cancelSource;
        private Task _collapseTaskRunning = Task.CompletedTask;
        private readonly bool _noStatus;

        private readonly static AsyncLocal<BackgroundTaskStatus<TStatus>?> s_asyncStatus = new();
        private volatile BackgroundTaskStatus<TStatus>? _status;

        private BackgroundTask<object> EventTask
        {
            get
            {
                if (_eventTaskLazy == null)
                    Interlocked.CompareExchange(ref _eventTaskLazy, new BackgroundTask<object>(Logger, true, true), null);
                return _eventTaskLazy;
            }
        }

        /// <summary>
        /// Gets or sets an optional logger for task exceptions
        /// </summary>
        public ILogger? Logger { get; set; }
        /// <summary>
        /// Occurs when state of current task changes
        /// </summary>
        public AsyncEvent StateChanged { get; }
        /// <summary>
        /// Occurs on exception in executed task
        /// </summary>
        public AsyncEvent<Exception> ExceptionHandler { get; }
        /// <summary>
        /// Status of current task
        /// </summary>
        public BackgroundTaskStatus<TStatus>? Status
        {
            get
            {
                if (_noStatus)
                    throw new InvalidOperationException("Status not enabled");
                return _status;
            }
        }
        /// <summary>
        /// Whether is the task currently running
        /// </summary>
        public bool IsRunning => _status?.IsCompleted == false;
        /// <summary>
        /// Whether was the last task cancelled
        /// </summary>
        public bool IsCancelled
        {
            get
            {
                lock (_syncRoot)
                {
                    return _cancelSource?.IsCancellationRequested == true;
                }
            }
        }

        public BackgroundTask(ILogger? logger = null, bool noStatus = false)
            : this(logger, false, noStatus)
        { }

        private BackgroundTask(ILogger? logger, bool isInternal, bool noStatus)
        {
            Logger = logger;
            _isInternal = isInternal;
            _noStatus = noStatus;
            if (!isInternal)
            {
                StateChanged = new();
                ExceptionHandler = new();
                _stateChangedInvoke = StateChanged.Invoke;
            }
            else
            {
                StateChanged = null!;
                ExceptionHandler = null!;
                _stateChangedInvoke = null!;
            }
        }

        /// <summary>
        /// Cancels all currently scheduled tasks
        /// </summary>
        public void Cancel()
        {
            lock (_syncRoot)
            {
                _cancelSource?.Cancel();
            }
        }

        private async Task ProcessCollapseTask(
            Delegate task,
            bool updateState,
            CancellationToken cancelFirst,
            CancellationToken cancelRest)
        { 
            var startedTimestamp = Stopwatch.GetTimestamp();
            try
            {
                if (!_noStatus)
                {
                    var status = new BackgroundTaskStatus<TStatus>(startedTimestamp, TimeSpan.Zero, 0, null, null, null);
                    s_asyncStatus.Value = status;
                    _status = status;
                }

                if (task is Func<CancellationToken, Task> funcTask)
                    await funcTask(cancelFirst);
                else if (task is Func<CancellationToken, ValueTask> funcValueTask)
                    await funcValueTask(cancelFirst);
                else
                    throw new InvalidOperationException("Invalid delegate passed");

                if (!_noStatus)
                    _status = new BackgroundTaskStatus<TStatus>(null, Stopwatch.GetElapsedTime(startedTimestamp), 100, null, null, null);
            }
            catch (Exception ex)
            {
                Logger?.LogError(ex, $"Unhandled exception in collapse task");
                await OnException(ex);
                if (!_noStatus)
                    _status = new BackgroundTaskStatus<TStatus>(null, Stopwatch.GetElapsedTime(startedTimestamp), 100, null, null, ex);
            }
            finally
            {
                if (!_noStatus)
                    s_asyncStatus.Value = null;
            }

            lock (_syncRoot)
            {
                if (_collapseTasks.Count != 0)
                {
                    var next = _collapseTasks[0];
                    _collapseTasks.RemoveAt(0);
                    _collapseTaskRunning = ProcessCollapseTask(next.factory, true, cancelRest, cancelRest);
                }
                else
                    _collapseTaskRunning = Task.CompletedTask;
            }
            if (updateState)
                await OnStateChanged();
        }

        private Task ProcessCollapseTask(
            bool updateState,
            CancellationToken cancelFirst,
            CancellationToken cancelRest)
        {
            lock (_syncRoot)
            {
                if (_collapseTasks.Count != 0)
                {
                    var next = _collapseTasks[0];
                    _collapseTasks.RemoveAt(0);
                    _collapseTaskRunning = ProcessCollapseTask(next.factory, true, cancelRest, cancelRest);
                }
                else
                    _collapseTaskRunning = Task.CompletedTask;
            }
            if (updateState)
                return OnStateChanged();
            else
                return Task.CompletedTask;
        }

        private Task OnException(Exception ex)
        {
            if (!_isInternal && ExceptionHandler.HasHandlers)
                return EventTask.StartValueTask(this, (cancel) => ExceptionHandler.Invoke(ex, cancel));
            else
                return Task.CompletedTask;
        }

        private Task OnStateChanged()
        {
            if (!_isInternal && StateChanged.HasHandlers)
                return EventTask.StartValueTask(this, _stateChangedInvoke);
            else
                return Task.CompletedTask;
        }

        /// <summary>
        /// Updates progress of currently executing task. Should be called from inside of currently running task.
        /// </summary>
        /// <exception cref="InvalidOperationException">When no operation is running</exception>
        public Task UpdateProgress(double done, double total, TimeSpan? estimate, TStatus? progressStatus)
        {
            if (_noStatus)
                throw new InvalidOperationException("Status not enabled");
            var status = s_asyncStatus.Value;
            if (status != null)
            {
                status = status.CloneWithProgress(total > 0 ? (float)(done * 100 / total) : 0, estimate, progressStatus);
                s_asyncStatus.Value = status;
                _status = status;
            }
            return OnStateChanged();
        }

        Task IStatusUpdater.UpdateProgress(double done, double total, TimeSpan? estimate, object? progressStatus)
            => UpdateProgress(done, total, estimate, (TStatus?)progressStatus);

        /// <summary>
        /// Queues new task. Delegates any exceptions to <see cref="OnException"/> or ctor logger. May block on synchronous part of the task/destroy, and for <see cref="StateChanged"/> event.
        /// </summary>
        public Task StartTask(object? type, Func<CancellationToken, Task> task, Func<CancellationToken, Task>? destroy = null, CancellationToken cancel = default)
            => StartInternal(type, task, destroy, cancel);

        /// <summary>
        /// Queues new task. Delegates any exceptions to <see cref="OnException"/> or ctor logger. May block on synchronous part of the task/destroy, and for <see cref="StateChanged"/> event.
        /// </summary>
        public Task StartValueTask(object? type, Func<CancellationToken, ValueTask> task, Func<CancellationToken, ValueTask>? destroy = null, CancellationToken cancel = default)
            => StartInternal(type, task, destroy, cancel);

        private Task StartInternal(object? type, Delegate task, Delegate? destroy, CancellationToken externalCancel)
        {
            CancellationToken cancelRest;
            Task? completedTask = null;
            lock (_syncRoot)
            {
                var isRunning = !_collapseTaskRunning.IsCompleted;
                if ((_collapseTasks.Count == 0 && !isRunning) || _cancelSource == null)
                {
                    if (_cancelSource is null or { IsCancellationRequested: true })
                        _cancelSource = new CancellationTokenSource();
                }
                if (_collapseTasks.Count != 0 && Equals(_collapseTasks[^1].type, type))
                {
                    var last = _collapseTasks[^1];
                    if (last.destroy != null)
                    {
                        _collapseTasks[^1] = (last.destroy, null, null);
                        _collapseTasks.Add((task, destroy, type));
                    }
                    else
                    {
                        _collapseTasks[^1] = (task, destroy, type);
                    }
                }
                else
                    _collapseTasks.Add((task, destroy, type));
                cancelRest = _cancelSource.Token;
                if (!isRunning)
                {
                    var cancelFirst = externalCancel == default
                        ? cancelRest
                        : CancellationTokenSource.CreateLinkedTokenSource(cancelRest, externalCancel).Token;
                    completedTask = _collapseTaskRunning;
                    _ = ProcessCollapseTask(false, cancelFirst, cancelRest); // will complete synchronously with these params
                }
            }
            if (completedTask == null)
                return Task.CompletedTask;
            else if (StateChanged is null or { HasHandlers: true }) // NOTE: null for internal tasks
                return completedTask;
            else
                return CompleteInternal(completedTask, cancelRest);
        }

        private async Task CompleteInternal(Task completedTask, CancellationToken cancel)
        {
            await completedTask;
            if (StateChanged != null) // NOTE: null for internal tasks
                await StateChanged.Invoke(cancel);
        }

        public async Task Wait(CancellationToken cancel = default)
        {
            while (true)
            {
                Task task;
                lock (_syncRoot)
                {
                    task = _collapseTaskRunning;
                    if (task.IsCompleted)
                        return;
                }
                await task.WaitAsync(cancel);
            }
        }
    }

    /// <summary>
    /// Ensures there at most two subsequent background tasks of same type actively running
    /// </summary>
    public class BackgroundTask : BackgroundTask<object>, IStatusUpdater
    {
        public BackgroundTask(ILogger? logger = null, bool noStatus = false)
            : base(logger, noStatus: noStatus)
        {
        }
    }
}
