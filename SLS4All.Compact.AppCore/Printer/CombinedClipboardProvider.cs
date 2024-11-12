// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

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
