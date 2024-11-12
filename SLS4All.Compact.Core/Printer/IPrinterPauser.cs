using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Threading
{
    public interface IPrinterPauser
    {
        bool IsPauseRequested { get; }
        ValueTask WaitIfPaused(CancellationToken cancel = default);
        ValueTask Pause(CancellationToken cancel = default);
        ValueTask Unpause(CancellationToken cancel = default);
    }
}
