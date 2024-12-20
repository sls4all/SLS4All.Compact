// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Printer;
using System.Runtime.CompilerServices;

namespace SLS4All.Compact.Helpers
{
    public class PrinterLocalRequestCultureProvider : RequestCultureProvider
    {
        private static readonly ConditionalWeakTable<object, ProviderCultureResult?> s_results = new();
        private readonly IOptionsMonitor<FrontendOptions> _options;
        private readonly IPrinterCultureManager _printerCultureManager;

        public PrinterLocalRequestCultureProvider(
            IOptionsMonitor<FrontendOptions> options,
            IPrinterCultureManager printerCultureManager)
        {
            _options = options;
            _printerCultureManager = printerCultureManager;
        }

        public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
        {
            ArgumentNullException.ThrowIfNull(httpContext);

            var httpContextDef = (DefaultHttpContext)httpContext;
            s_results.TryGetValue(httpContextDef.ServiceScopeFactory, out var result);

            var request = httpContext.Request;
            if (!request.QueryString.HasValue)
                return result;

            var options = _options.CurrentValue;
            string? localSessionKey = request.Query[FrontendHelpers.LocalSessionKey];
            if (string.IsNullOrEmpty(localSessionKey) || localSessionKey.Equals(options.LocalSessionKey) == false)
                return result;

            var culture = await _printerCultureManager.GetPrinterCulture();
            if (culture != null) // should not happen due to arg in GetPrinterCulture
            {
                result = new ProviderCultureResult(culture.Name);
                s_results.AddOrUpdate(httpContextDef.ServiceScopeFactory, result);
                return result;
            }
            else
            {
                s_results.Remove(httpContextDef.ServiceScopeFactory);
                return result;
            }
        }
    }
}
