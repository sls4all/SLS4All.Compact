// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Azure;
using Lexical.FileProvider.Package;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SLS4All.Compact.Helpers
{
    public class StreamingHelperOptions
    {
        public TimeSpan CleanupPeriod { get; set; } = TimeSpan.FromSeconds(5);
    }

    public abstract class StreamingHelperBase<T> : BackgroundThreadService
    {
        private sealed class PullData
        {
            public required AsyncEvent<T> CapturedEvent { get; init; }
            public SystemTimestamp Timestamp;
            public T Data = default!;
            public Func<T, CancellationToken, ValueTask>? Handler;
        }

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<StreamingHelperOptions> _options;
        protected readonly ConcurrentDictionary<string, CancellationTokenSource> _streamCancels;
        private readonly Lock _locker;
        private readonly Dictionary<AsyncEvent<T>, PullData> _pullDataByEvent;
        private readonly Dictionary<string, PullData> _pullDataById;
        private readonly T _streamingPlaceholder;

        protected StreamingHelperBase(
            ILogger logger,
            IOptionsMonitor<StreamingHelperOptions> options,
            T placeholder)
            : base(logger)
        {
            _logger = logger;
            _options = options;

            _streamCancels = new();
            _locker = new();
            _pullDataByEvent = new();
            _pullDataById = new();
            _streamingPlaceholder = placeholder;
        }

        public bool TryGetCapturedEvent(string id, [MaybeNullWhen(false)] out AsyncEvent<T> imageReadyEvent)
        {
            lock (_locker)
            {
                if (_pullDataById.TryGetValue(id, out var pullData))
                {
                    imageReadyEvent = pullData.CapturedEvent;
                    return true;
                }
                else
                {
                    imageReadyEvent = null;
                    return false;
                }
            }
        }

        public void SetPulled(string id)
        {
            lock (_locker)
            {
                if (_pullDataById.TryGetValue(id, out var pullData))
                    pullData.Timestamp = SystemTimestamp.Now;
            }
        }

        public async Task Pull(
            string id,
            IDataGenerator<T> generator,
            HttpResponse response,
            CancellationToken cancel)
        {
            if (!generator.TryRentLastValue(out var data))
                data = _streamingPlaceholder;
            var imageCaptured = generator.Captured;
            lock (_locker)
            {
                var now = SystemTimestamp.Now;
                if (!_pullDataByEvent.TryGetValue(imageCaptured, out var pullData))
                {
                    if (!_pullDataByEvent.TryGetValue(imageCaptured, out pullData))
                    {
                        pullData = new PullData
                        {
                            CapturedEvent = imageCaptured,
                        };
                        _pullDataByEvent.TryAdd(imageCaptured, pullData);
                    }
                }

                _pullDataById[id] = pullData;

                pullData.Timestamp = now;

                if (pullData.Handler == null)
                {
                    pullData.Handler = (image, cancel) =>
                    {
                        var copy = RentCopy(image);
                        lock (_locker)
                        {
                            Return(pullData.Data);
                            pullData.Data = copy;
                        }
                        return ValueTask.CompletedTask;
                    };

                    imageCaptured.AddHandler(pullData.Handler);
                }

                if (!IsEmpty(pullData.Data))
                {
                    Return(data);
                    data = RentCopy(pullData.Data);
                }
            }

            await Write(data, response, cancel);
            Return(data);
        }

        public void Keepalive(
            string id,
            IDataGenerator<T> generator)
        {
            var imageCaptured = generator.Captured;
            lock (_locker)
            {
                var now = SystemTimestamp.Now;
                if (!_pullDataByEvent.TryGetValue(imageCaptured, out var pullData))
                {
                    if (!_pullDataByEvent.TryGetValue(imageCaptured, out pullData))
                    {
                        pullData = new PullData
                        {
                            CapturedEvent = imageCaptured,
                        };
                        _pullDataByEvent.TryAdd(imageCaptured, pullData);
                    }
                }

                _pullDataById[id] = pullData;

                pullData.Timestamp = now;

                if (pullData.Handler == null)
                {
                    pullData.Handler = (image, cancel) =>
                    {
                        var copy = RentCopy(image);
                        lock (_locker)
                        {
                            Return(pullData.Data);
                            pullData.Data = copy;
                        }
                        return ValueTask.CompletedTask;
                    };

                    imageCaptured.AddHandler(pullData.Handler);
                }
            }
        }

        public abstract ValueTask Write(T data, HttpResponse response, CancellationToken cancel);

        protected abstract T RentCopy(T data);
        protected abstract void Return(T data);
        protected abstract bool IsEmpty(T data);

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            var timer = new PeriodicTimer(options.CleanupPeriod);
            var remainingDataById = new HashSet<PullData>();
            var removedIds = new HashSet<string>();
            var removedEvents = new HashSet<AsyncEvent<T>>();
            while (await timer.WaitForNextTickAsync(cancel))
            {
                var expiredAt = SystemTimestamp.Now - options.CleanupPeriod;
                remainingDataById.Clear();
                removedIds.Clear();
                removedEvents.Clear();
                lock (_locker)
                {
                    foreach ((var id, var pullData) in _pullDataById)
                    {
                        if (pullData.Timestamp < expiredAt || pullData.Handler == null)
                            removedIds.Add(id);
                    }

                    foreach (var id in removedIds)
                        _pullDataById.Remove(id);

                    foreach (var pullData in _pullDataById.Values)
                        remainingDataById.Add(pullData);

                    foreach ((var ev, var pullData) in _pullDataByEvent)
                    {
                        if (!remainingDataById.Contains(pullData))
                        {
                            if (pullData.Handler != null)
                            {
                                pullData.CapturedEvent.RemoveHandler(pullData.Handler);
                                pullData.Handler = null;
                            }
                            removedEvents.Add(ev);
                        }
                    }

                    foreach (var ev in removedEvents)
                        _pullDataByEvent.Remove(ev);
                }
            }
        }
    }
}
