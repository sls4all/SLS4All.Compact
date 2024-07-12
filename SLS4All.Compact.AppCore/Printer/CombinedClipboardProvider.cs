using Microsoft.JSInterop;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Printer
{
    public class CombinedClipboardProvider : MemoryClipboardProvider
    {
        private readonly IJSRuntime _jsRuntime;

        public CombinedClipboardProvider(IJSRuntime jsRuntime)
        {
            _jsRuntime = jsRuntime;
        }

        public override async ValueTask Copy(object? obj, string? str, CancellationToken cancel)
        {
            try
            {
                await _jsRuntime.InvokeVoidAsync("AppHelpersInvoke", "copyTextToClipboard", str ?? "");
            }
            catch (Exception) when (!cancel.IsCancellationRequested)
            {
                // swallow
            }
            await base.Copy(obj, str, cancel);
        }

    }
}
