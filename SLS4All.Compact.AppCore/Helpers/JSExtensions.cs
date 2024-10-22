// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace SLS4All.Compact.Helpers
{
    public static class JSExtensions
    {
        public static async ValueTask<bool> TrySelectAll(this ElementReference element, IJSRuntime runtime)
        {
            try
            {
                await runtime.InvokeVoidAsync("AppHelpersInvoke", "selectAll", element);
            }
            catch (JSException)
            {
                // swallow
            }
            return false;
        }

        public static async ValueTask<bool> TryFocusAsync(this ElementReference element)
        {
            try
            {
                await element.FocusAsync();
                return true;
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is JSException)
            {
                // swallow
            }
            return false;
        }

        public static async ValueTask<bool> TryScrollIntoView(this ElementReference element, IJSRuntime runtime)
        {
            try
            {
                return await runtime.InvokeAsync<bool>("AppHelpersInvoke", "scrollElementIntoView", element);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is JSException)
            {
                // swallow
            }
            return false;
        }

        public static async ValueTask<bool> IsScrolledIntoView(this ElementReference element, IJSRuntime runtime)
        {
            try
            {
                return await runtime.InvokeAsync<bool>("AppHelpersInvoke", "isScrolledIntoView", element);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is JSException)
            {
                // swallow
            }
            return false;
        }
    }
}
