using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SLS4All.Compact.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static SLS4All.Compact.ApplicationOptions;

namespace SLS4All.Compact.Components
{
    public class RedirectingRouteView : RouteView
    {
        private RouteData _originalRouteData = default!;

        [Inject]
        public IPageRedirector Redirector { get; set; } = default!;

        [EditorRequired]
        [Parameter]
        public RouteData OriginalRouteData
        {
            get => _originalRouteData;
            set
            {
                _originalRouteData = value;
                var redirectedType = Redirector.GetTarget(value.PageType);
                if (redirectedType != value.PageType)
                    base.RouteData = new RouteData(redirectedType, value.RouteValues);
                else
                    base.RouteData = value;
            }
        }
    }
}
