// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using Nito.AsyncEx;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Graphics;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace SLS4All.Compact.ComponentModel
{
    public class AppThemeManagerSavedOptions
    {
        public string ThemeId { get; set; } = "";

        public AppThemeManagerSavedOptions Clone()
            => (AppThemeManagerSavedOptions)MemberwiseClone();
    }

    public sealed class AppThemeManager
    {
        private readonly IOptionsMonitor<AppThemeManagerSavedOptions> _savedOptions;
        private readonly IOptionsWriter<AppThemeManagerSavedOptions> _savedOptionsWriter;
        private readonly AsyncLock _lock = new();

        public string ThemeIdDefault => "3";
        public string[] AllThemeIds { get; } = new[] { "1", "2", "3", "4", "5", "6", "7", "8", "9", "10" };
        public string ThemeId
        {
            get
            {
                var themeId = _savedOptions.CurrentValue.ThemeId;
                if (!string.IsNullOrEmpty(themeId))
                    return themeId;
                else
                    return ThemeIdDefault;
            }
        }
        public AsyncEvent ThemeChanged { get; } = new();

        public AppThemeManager(
            IOptionsMonitor<AppThemeManagerSavedOptions> savedOptions,
            IOptionsWriter<AppThemeManagerSavedOptions> savedOptionsWriter)
        {
            _savedOptions = savedOptions;
            _savedOptionsWriter = savedOptionsWriter;
        }

        public async Task SetTheme(string id, CancellationToken cancel)
        {
            using (await _lock.LockAsync(cancel))
            {
                var value = _savedOptions.CurrentValue.Clone();
                if (value.ThemeId != id)
                {
                    value.ThemeId = id;
                    await _savedOptionsWriter.Write(value, cancel);
                    await ThemeChanged.Invoke(cancel);
                }
            }
        }

        public RgbaF GetBackgroundColor(string theme)
            => theme switch
            {
                "1" => new RgbaF("7f3639"),
                "2" => new RgbaF("32334c"),
                "3" => new RgbaF("0c2b2b"),
                "4" => new RgbaF("2f313a"),
                "5" => new RgbaF("514a48"),
                "6" => new RgbaF("644731"),
                "7" => new RgbaF("335665"),
                "8" => new RgbaF("64644e"),
                "9" => new RgbaF("2f674b"),
                "10" => new RgbaF("6f4965"),
                _ => new RgbaF("0c2b2b"),
            };
    }
}
