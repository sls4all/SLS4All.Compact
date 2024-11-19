using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public class PrinterPauser : IPrinterPauser
    {
        private readonly Lock _sync = new();
        private TaskCompletionSource? _pauseTaskSource;
        private volatile Task _pauseTask;

        public bool IsPauseRequested => !_pauseTask.IsCompleted;

        public PrinterPauser()
        {
            _pauseTask = Task.CompletedTask;
        }

        public ValueTask Pause(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_pauseTaskSource == null)
                {
                    _pauseTaskSource = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    _pauseTask = _pauseTaskSource.Task;
                }
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask Unpause(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            lock (_sync)
            {
                if (_pauseTaskSource != null)
                {
                    _pauseTaskSource.SetResult();
                    _pauseTask = _pauseTaskSource.Task;
                    _pauseTaskSource = null;
                }
            }
            return ValueTask.CompletedTask;
        }

        public ValueTask WaitIfPaused(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var task = _pauseTask;
            if (task.IsCompleted)
                return ValueTask.CompletedTask;
            else
                return new ValueTask(task.WaitAsync(cancel));
        }
    }
}
