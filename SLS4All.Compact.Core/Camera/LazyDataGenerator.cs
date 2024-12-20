// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Dynamic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public abstract class LazyDataGenerator<T> : IDisposable, IDataGenerator<T>
    {
        private sealed class StartScopeHelper(LazyDataGenerator<T> owner) : IDisposable
        {
            private LazyDataGenerator<T>? _owner = owner;

            public void Dispose()
            {
                var owner = _owner;
                if (owner != null)
                {
                    _owner = null;
                    owner.ExitStartScope();
                }
            }
        }

        private readonly ILogger _logger;
        private readonly Lock _lastMimeLock = new();
        private readonly Lock _startLock = new();
        private readonly TaskQueue _startQueue = new();
        private T _lastValue;
        private bool _generatorRunning;
        private CancellationTokenSource _cancelSource;
        private bool _isStarted;
        private int _startScopeCount;

        public AsyncEvent<T> Captured { get; } = new();

        public LazyDataGenerator(
            ILogger logger)
        {
            _logger = logger;
            _cancelSource = new();
            _lastValue = default!;
            Captured.HandlersChanged += OnHandlersChanged;
        }

        protected abstract void ReleaseTemporaryResources();

        public void RestartGeneratorIfRunning()
        {
            lock (_startLock)
            {
                if (_isStarted)
                {
                    _logger.LogInformation($"Restarting generator");
                    _cancelSource.Cancel();
                    _isStarted = false;
                }
                CheckStartGenerator();
            }
        }

        private void CheckStartGenerator()
        { 
            lock (_startLock)
            {
                if (Captured.HandlersCount == 0 && _startScopeCount == 0) // no handlers, no scopes
                {
                    _logger.LogInformation($"Stopping generator");
                    _cancelSource.Cancel();
                    _isStarted = false;
                    ReleaseTemporaryResources();
                }
                else if (!_isStarted) // handlers present, not initialized
                {
                    _logger.LogInformation($"Starting generator");
                    var cancelSource = new CancellationTokenSource();
                    _cancelSource = cancelSource;
                    _isStarted = true;
                    _startQueue.Enqueue(() =>
                    {
                        return Task.Factory.StartNew(
                            state => RunGenerator(cancelSource.Token),
                            null,
                            default,
                            TaskCreationOptions.LongRunning,
                            TaskScheduler.Default).Unwrap();
                    }, null, false);
                }
            }
        }

        private void OnHandlersChanged(object? sender, EventArgs e)
        {
            CheckStartGenerator();
        }

        private async Task RunGenerator(CancellationToken cancel)
        {
            try
            {
                lock (_lastMimeLock)
                {
                    _generatorRunning = true;
                }
                cancel.ThrowIfCancellationRequested();
                await RunGeneratorOverride(cancel);
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    _logger.LogError(ex, $"Failed to start/run generator task");
            }
            finally
            {
                lock (_lastMimeLock)
                {
                    _generatorRunning = false;
                    _lastValue = default!;
                }
                _logger.LogDebug($"Generator stopped");
            }
        }

        protected ValueTask OnCaptured(T data, CancellationToken cancel)
        {
            lock (_lastMimeLock)
            {
                if (_generatorRunning)
                {
                    Return(_lastValue);
                    _lastValue = RentCopy(data);
                }
            }
            return Captured.Invoke(data, cancel);
        }

        protected abstract Task RunGeneratorOverride(CancellationToken cancel);

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            Captured.HandlersChanged -= OnHandlersChanged;
            _cancelSource.Cancel();
            ReleaseTemporaryResources();
        }

        public bool TryRentLastValue(out T data)
        {
            lock (_lastMimeLock)
            {
                data = RentCopy(_lastValue);
                return !IsEmpty(data);
            }
        }

        protected virtual T RentCopy(T data)
            => data;
        protected virtual void Return(T data)
        {
        }
        protected virtual bool IsEmpty(T data)
            => EqualityComparer<T>.Default.Equals(data, default);

        public IDisposable StartScope()
        {
            try
            {
                lock (_startLock)
                {
                    Debug.Assert(_startScopeCount >= 0);
                    _startScopeCount++;
                    CheckStartGenerator();
                }
                return new StartScopeHelper(this);
            }
            catch
            {
                ExitStartScope();
                throw;
            }
        }

        private void ExitStartScope()
        {
            lock (_startLock)
            {
                Debug.Assert(_startScopeCount > 0);
                _startScopeCount--;
                CheckStartGenerator();
            }
        }
    }
}
