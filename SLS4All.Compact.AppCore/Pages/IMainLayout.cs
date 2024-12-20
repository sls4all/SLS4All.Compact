// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System.Globalization;
using System.Numerics;
using Microsoft.AspNetCore.Components;
using SLS4All.Compact.Components;
using SLS4All.Compact.Graphics;

namespace SLS4All.Compact.Pages
{
    public interface IMainLayout
    {
        void SetTitle(PrinterPageTitle? title);
        Task AppExitShowLoader();
        Vector2 Scale { get; }
        RgbaF BackgroundColor { get; }
        bool IsLocalSession { get; }
        bool IsDeveloperMode { get; set; }

        Task BrowseFiles();
        string GetReloadUri(bool forceReload, string? relative = null);
    }
}
