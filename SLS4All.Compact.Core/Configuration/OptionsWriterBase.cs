// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Options;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Threading;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public abstract class OptionsWriterBase<T> : IOptionsWriter<T>
    {
        private sealed class DisposableWrapper : IDisposable
        {
            private readonly OptionsWriterBase<T> _writer;
            private readonly TaskQueue _taskQueue;
            private Action<T>? _listener;
            private T _lastCurrent;
            private IDisposable? _innerHandler;

            public DisposableWrapper(OptionsWriterBase<T> writer, Action<T> listener)
            {
                _taskQueue = new TaskQueue();
                _listener = listener;
                _writer = writer;
                _lastCurrent = writer.Clone(writer._options.CurrentValue);
                _innerHandler = writer._options.OnChange(OnChange);
            }

            public void OnChange(T value, string? arg)
            {
                _taskQueue.Enqueue(() =>
                {
                    if (!_writer.Equals(_lastCurrent, value))
                    {
                        _lastCurrent = _writer.Clone(value);
                        _listener?.Invoke(value);
                    }
                    return Task.CompletedTask;
                }, null, doThrow: true);
            }

            public void Dispose()
            {
                _listener = null;
                _innerHandler?.Dispose(); 
            }
        }

        private readonly IOptionsMonitor<T> _options;

        public T CurrentValue => _options.CurrentValue;
            
        protected OptionsWriterBase(IOptionsMonitor<T> options)
        {
            _options = options;
        }

        public abstract Task Write(T newValue, CancellationToken cancel);

        public abstract bool Equals(T x, T y);

        public abstract T Clone(T obj);

        public IDisposable? OnChange(Action<T> listener)
            => new DisposableWrapper(this, listener);

        public T Get(string? name)
            => _options.Get(name);
    }
}