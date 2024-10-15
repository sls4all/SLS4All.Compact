using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public interface IPrinterSafetySessionManager
    {
        Task<bool> CheckReadyForSafetySession(CancellationToken cancel);
        Task<IAsyncDisposable> BeginSafetySession(CancellationToken cancel);
    }
}
