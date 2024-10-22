using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.DependencyInjection;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;

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

    public interface IPageRedirector
    {
        Type GetTarget(Type pageType);
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
