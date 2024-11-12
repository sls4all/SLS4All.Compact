// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using SLS4All.Compact.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
