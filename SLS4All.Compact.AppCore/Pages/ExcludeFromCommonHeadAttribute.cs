using Microsoft.AspNetCore.Components.Endpoints;
using Microsoft.AspNetCore.Http;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Pages
{
    [AttributeUsage(AttributeTargets.Class)]
    public class ExcludeFromCommonHeadAttribute : Attribute
    {
    }

    public static class ExcludeFromCommonHeadHelper
    {
        private static readonly ConcurrentDictionary<Type, bool> s_cache = new();

        public static bool ExcludeFromCommonHead(this HttpContext context)
        {
            ArgumentNullException.ThrowIfNull(context);
            var pageType = context.GetEndpoint()?.Metadata.GetMetadata<ComponentTypeMetadata>()?.Type;
            return pageType is not null
                && s_cache.GetOrAdd(
                    pageType,
                    static pageType => pageType.IsDefined(typeof(ExcludeFromCommonHeadAttribute), true));
        }

        internal static class MetadataUpdateHandler
        {
            /// <summary>
            /// Invoked as part of <see cref="MetadataUpdateHandlerAttribute" /> contract for hot reload.
            /// </summary>
            public static void ClearCache(Type[]? _)
                => s_cache.Clear();
        }
    }
}
