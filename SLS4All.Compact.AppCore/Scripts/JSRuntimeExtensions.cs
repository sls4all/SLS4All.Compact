// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.JSInterop;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace SLS4All.Compact.Scripts
{
    public static class JSRuntimeExtensions
    {
        private readonly static ConditionalWeakTable<IJSRuntime, IJSRuntime> _cache = new();

        public static bool GuessIsInitialized(this IJSRuntime runtime)
        {
            if (_cache.TryGetValue(runtime, out _))
                return true;
            bool isInitialized;
            if (runtime.GetType().FullName == "Microsoft.AspNetCore.Components.Server.Circuits.RemoteJSRuntime")
                isInitialized = (bool)runtime.GetType().InvokeMember("IsInitialized", BindingFlags.Public | BindingFlags.Instance | BindingFlags.GetProperty, null, runtime, null)!;
            else
                isInitialized = true; // just a guess
            if (isInitialized)
                _cache.Add(runtime, runtime);
            return isInitialized;
        }

        public static async ValueTask CollectGarbage(this IJSRuntime runtime, CancellationToken cancel = default)
        {
            try
            {
                await runtime.InvokeVoidAsync("AppHelpersInvoke", cancel, "collectGarbage").AsTask().WaitAsync(TimeSpan.FromSeconds(1), cancel).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or JSDisconnectedException or TimeoutException)
            {
                // swallow
            }
        }
    }
}
