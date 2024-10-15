// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Storage.PrinterSettings;
using System.Collections.Concurrent;

namespace SLS4All.Compact.Pages
{
    public abstract class AppPage : ComponentBase, IAsyncDisposable
    {
        private readonly static ConcurrentDictionary<AppPage, bool> _activePages = new();

        public static IEnumerable<AppPage> ActivePages => _activePages.Keys;
        private PrinterLocalizationSettings _localization = new();

        protected PrinterLocalizationSettings LocalizationSettings => _localization;

        [Inject]
        private ILogger<AppPage> Logger { get; set; } = null!;

        [CascadingParameter]
        public IMainLayout? MainLayout { get; set; }

        [Inject]
        public NavigationManager NavigationManager { get; set; } = default!;

        [Inject]
        public IPrinterSettingsStorage PrinterSettingsStorage { get; set; } = default!;

        [Inject]
        public IUnitConverter UnitConverter { get; set; } = default!;

        public string GetReloadUri()
            => MainLayout!.GetReloadUri();

        protected override async Task OnInitializedAsync()
        {
            _localization = PrinterSettingsStorage.GetLocalizationSettings();
            await base.OnInitializedAsync();
            _activePages.TryAdd(this, false);
        }

        public virtual ValueTask DisposeAsync()
        {
            _activePages.TryRemove(this, out _);
            return ValueTask.CompletedTask;
        }

        public void TryInvokeStateHasChanged(CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(cancel);

        public Task TryInvokeStateHasChangedAsync(CancellationToken cancel = default)
        {
            try
            {
                return InvokeAsync(() =>
                {
                    try
                    {
                        StateHasChanged();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Failed to update state");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to update state");
                return Task.CompletedTask;
            }
        }

        public void TryInvokeStateHasChanged(Func<ValueTask> action, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, cancel);

        public Task TryInvokeStateHasChangedAsync(Func<ValueTask> action, CancellationToken cancel = default)
        {
            try
            {
                return InvokeAsync(async () =>
                {
                    try
                    {
                        await action();
                        StateHasChanged();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Failed to update state");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to update state");
                return Task.CompletedTask;
            }
        }

        public void TryInvokeStateHasChanged(Func<ValueTask<bool>> action, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, cancel);

        public Task TryInvokeStateHasChangedAsync(Func<ValueTask<bool>> action, CancellationToken cancel = default)
        {
            try
            {
                return InvokeAsync(async () =>
                {
                    try
                    {
                        if (await action())
                            StateHasChanged();
                    }
                    catch (Exception ex)
                    {
                        Logger.LogError(ex, $"Failed to update state");
                    }
                });
            }
            catch (Exception ex)
            {
                Logger.LogError(ex, $"Failed to update state");
                return Task.CompletedTask;
            }
        }

        protected UnitValue? GetUnits(double? value, string unit)
            => value != null ? GetUnits(value.Value, unit) : null;

        protected UnitValue GetUnits(double value, string unit)
            => GetUnits((decimal)value, unit);

        protected UnitValue GetUnits(decimal value, string unit)
            => UnitConverter.GetUnits(value, unit, _localization.UnitConverterFlags);
    }
}
