// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public sealed class AsyncEvent
    {
        private static readonly Delegate _noHandlerDelegate = () => { };
        private readonly Lock _handlersChangedSync = new();
        private readonly ConcurrentDictionary<Delegate, long> _handlers = new();
        private readonly bool _ordered;
        private volatile Delegate? _singleHandler;
        private long _counter;

        public bool HasHandlers => _singleHandler != _noHandlerDelegate;
        public int HandlersCount => _handlers.Count;
        public event EventHandler? HandlersChanged;
        public Lock HandlersChangedSync => _handlersChangedSync;

        public AsyncEvent(bool ordered = false)
        {
            _ordered = ordered;
            _singleHandler = _noHandlerDelegate;
        }

        private void OnHandlerChangedInner()
            => HandlersChanged?.Invoke(this, EventArgs.Empty);

        public ValueTask Invoke(CancellationToken cancel)
        {
            var singleHandler = _singleHandler;
            if (singleHandler == _noHandlerDelegate)
            {
                cancel.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }
            else if (singleHandler != null)
            {
                cancel.ThrowIfCancellationRequested();
                if (singleHandler is Func<CancellationToken, ValueTask> valueTaskFunc)
                    return valueTaskFunc(cancel);
                else
                    return new ValueTask(((Func<CancellationToken, Task>)singleHandler)(cancel));
            }
            else
                return InvokeInner(cancel);
        }

        private async ValueTask InvokeInner(CancellationToken cancel)
        {
            if (_ordered)
            {
                foreach (var handler in _handlers.OrderBy(x => x.Value))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (handler.Key is Func<CancellationToken, ValueTask> valueTaskFunc)
                        await valueTaskFunc(cancel);
                    else
                        await ((Func<CancellationToken, Task>)handler.Key)(cancel);
                }
            }
            else
            {
                foreach (var handler in _handlers.Keys)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (handler is Func<CancellationToken, ValueTask> valueTaskFunc)
                        await valueTaskFunc(cancel);
                    else
                        await ((Func<CancellationToken, Task>)handler)(cancel);
                }
            }
        }

        public void AddHandler(Func<CancellationToken, ValueTask> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryAdd(handler, _counter++);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => handler, _ => null };
                OnHandlerChangedInner();
            }
        }

        public void AddHandler(Func<CancellationToken, Task> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryAdd(handler, _counter++);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => handler, _ => null };
                OnHandlerChangedInner();
            }
        }

        public void RemoveHandler(Func<CancellationToken, ValueTask> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryRemove(handler, out _);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => _handlers.Keys.Single(), _ => null };
                OnHandlerChangedInner();
            }
        }

        public void RemoveHandler(Func<CancellationToken, Task> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryRemove(handler, out _);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => _handlers.Keys.Single(), _ => null };
                OnHandlerChangedInner();
            }
        }
    }

    public sealed class AsyncEvent<TArg1>
    {
        private static readonly Delegate _noHandlerDelegate = () => { };
        private readonly Lock _handlersChangedSync = new();
        private readonly ConcurrentDictionary<Delegate, long> _handlers = new();
        private readonly bool _ordered;
        private volatile Delegate? _singleHandler;
        private long _counter;

        public bool HasHandlers => _singleHandler != _noHandlerDelegate;
        public int HandlersCount => _handlers.Count;
        public event EventHandler? HandlersChanged;
        public Lock HandlersChangedSync => _handlersChangedSync;

        public AsyncEvent(bool ordered = false)
        {
            _ordered = ordered;
            _singleHandler = _noHandlerDelegate;
        }

        private void OnHandlerChangedInner()
            => HandlersChanged?.Invoke(this, EventArgs.Empty);

        public ValueTask Invoke(TArg1 arg1, CancellationToken cancel)
        {
            var singleHandler = _singleHandler;
            if (singleHandler == _noHandlerDelegate)
            {
                cancel.ThrowIfCancellationRequested();
                return ValueTask.CompletedTask;
            }
            else if (singleHandler != null)
            {
                cancel.ThrowIfCancellationRequested();
                if (singleHandler is Func<TArg1, CancellationToken, ValueTask> valueTaskFunc)
                    return valueTaskFunc(arg1, cancel);
                else
                    return new ValueTask(((Func<TArg1, CancellationToken, Task>)singleHandler)(arg1, cancel));
            }
            else
                return InvokeInner(arg1, cancel);
        }

        private async ValueTask InvokeInner(TArg1 arg1, CancellationToken cancel)
        {
            if (_ordered)
            {
                foreach (var handler in _handlers.OrderBy(x => x.Value))
                {
                    cancel.ThrowIfCancellationRequested();
                    if (handler.Key is Func<TArg1, CancellationToken, ValueTask> valueTaskFunc)
                        await valueTaskFunc(arg1, cancel);
                    else
                        await ((Func<TArg1, CancellationToken, Task>)handler.Key)(arg1, cancel);
                }
            }
            else
            {
                foreach (var handler in _handlers.Keys)
                {
                    cancel.ThrowIfCancellationRequested();
                    if (handler is Func<TArg1, CancellationToken, ValueTask> valueTaskFunc)
                        await valueTaskFunc(arg1, cancel);
                    else
                        await ((Func<TArg1, CancellationToken, Task>)handler)(arg1, cancel);
                }
            }
        }

        public void AddHandler(Func<TArg1, CancellationToken, ValueTask> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryAdd(handler, _counter++);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => handler, _ => null };
                OnHandlerChangedInner();
            }
        }

        public void AddHandler(Func<TArg1, CancellationToken, Task> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryAdd(handler, _counter++);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => handler, _ => null };
                OnHandlerChangedInner();
            }
        }

        public void RemoveHandler(Func<TArg1, CancellationToken, ValueTask> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryRemove(handler, out _);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => _handlers.Keys.Single(), _ => null };
                OnHandlerChangedInner();
            }
        }

        public void RemoveHandler(Func<TArg1, CancellationToken, Task> handler)
        {
            lock (_handlersChangedSync)
            {
                _handlers.TryRemove(handler, out _);
                _singleHandler = _handlers.Count switch { 0 => _noHandlerDelegate, 1 => _handlers.Keys.Single(), _ => null };
                OnHandlerChangedInner();
            }
        }
    }
}
