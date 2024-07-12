// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using System.Reflection.Emit;

namespace SLS4All.Compact.Components
{
    public abstract class AppComponent : ComponentBase, IAsyncDisposable
    {
        private Dictionary<string, object>? _attributesLazy;
        [Inject]
        private ILogger<AppComponent> Logger { get; set; } = null!;

        [Parameter]
        public string? ExternalCssScope { get; set; }
        public bool HasAttributes => _attributesLazy?.Count > 0;
        [Parameter(CaptureUnmatchedValues = true)]
        public Dictionary<string, object> Attributes
        {
            get => _attributesLazy ??= new();
            set => _attributesLazy = value;
        }
        [Parameter]
        public string? ElementId { get; set; }

        public ElementReference ElementRef { get; set; }
        protected KeyValuePair<string, object>[] AttributesWithCssScope { get; private set; } = Array.Empty<KeyValuePair<string, object>>();
        protected KeyValuePair<string, object>[] JustAttributesWithCssScope { get; private set; } = Array.Empty<KeyValuePair<string, object>>();

        protected string? ClassNames { get; private set; }
        protected string? StyleNames { get; private set; }

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

        public void TryInvokeStateHasChanged(Func<ValueTask>? action, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, cancel);

        public void TryInvokeStateHasChanged(Func<ValueTask>? action, Func<ValueTask>? postAction, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, postAction, cancel);

        public Task TryInvokeStateHasChangedAsync(Func<ValueTask>? action, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, null, cancel);

        public Task TryInvokeStateHasChangedAsync(Func<ValueTask>? action, Func<ValueTask>? postAction, CancellationToken cancel = default)
        {
            try
            {
                return InvokeAsync(async () =>
                {
                    try
                    {
                        if (action != null)
                            await action();
                        StateHasChanged();
                        if (postAction != null)
                            await postAction();
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

        public void TryInvokeStateHasChanged(Func<ValueTask<bool>>? action, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, cancel);

        public void TryInvokeStateHasChanged(Func<ValueTask<bool>>? action, Func<ValueTask>? postAction, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, postAction, cancel);

        public Task TryInvokeStateHasChangedAsync(Func<ValueTask<bool>>? action, CancellationToken cancel = default)
            => TryInvokeStateHasChangedAsync(action, null, cancel);

        public Task TryInvokeStateHasChangedAsync(Func<ValueTask<bool>>? action, Func<ValueTask>? postAction, CancellationToken cancel = default)
        {
            try
            {
                return InvokeAsync(async () =>
                {
                    try
                    {
                        if (action != null)
                            if (!await action())
                                return;
                        StateHasChanged();
                        if (postAction != null)
                            await postAction();
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

        protected override void OnParametersSet()
        {
            base.OnParametersSet();
            if (HasAttributes)
            {
                if (Attributes.TryGetValue("class", out var classes))
                {
                    Attributes.Remove("class");
                    ClassNames = classes?.ToString();
                }
                if (Attributes.TryGetValue("style", out var styles))
                {
                    Attributes.Remove("style");
                    StyleNames = styles?.ToString();
                }
            }
            if (!string.IsNullOrEmpty(ExternalCssScope))
            {
                var pair = new KeyValuePair<string, object>(ExternalCssScope, ExternalCssScope);
                JustAttributesWithCssScope = new[] { pair };
                if (HasAttributes)
                    AttributesWithCssScope = Attributes.Append(pair).ToArray();
                else
                    AttributesWithCssScope = JustAttributesWithCssScope;
            }
            else
            {
                AttributesWithCssScope = Attributes.ToArray();
                JustAttributesWithCssScope = Array.Empty<KeyValuePair<string, object>>();
            }
        }

        public virtual ValueTask DisposeAsync()
            => ValueTask.CompletedTask;
    }
}
