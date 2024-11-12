// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using System.Collections.Frozen;

namespace SLS4All.Compact.Components
{
    public class PageRedirectorOptions
    {
        public class RedirectInfo : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public required string Original { get; set; }
            public required string Replacement { get; set; }
        }

        public Dictionary<string, RedirectInfo?> Redirects { get; set; } = new();
    }

    public class PageRedirector : IPageRedirector
    {
        private readonly IOptions<PageRedirectorOptions> _options;
        private readonly FrozenDictionary<Type, Type> _redirects;

        public PageRedirector(IOptions<PageRedirectorOptions> options)
        {
            _options = options;
            var o = options.Value;
            var redirects = new Dictionary<Type, Type>();
            foreach ((var key, var replacememt) in o.Redirects.GetOrderedEnabledKeyValues())
            {
                if (string.IsNullOrWhiteSpace(replacememt.Original))
                    throw new InvalidOperationException($"Page redirect with key {key} does not have set original type");
                if (string.IsNullOrWhiteSpace(replacememt.Replacement))
                    throw new InvalidOperationException($"Page redirect with key {key} does not have set replacement type");
                var originalType = Type.GetType(replacememt.Original, true)!;
                var replacementType = Type.GetType(replacememt.Replacement, true)!;
                redirects[originalType] = replacementType;
            }
            _redirects = redirects.ToFrozenDictionary();
        }

        public Type GetTarget(Type pageType)
            => _redirects.TryGetValue(pageType, out var redirectType) ? redirectType : pageType;
    }
}
