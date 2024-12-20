// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.Logging;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Pages;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Components
{
    public sealed class BodyErrorBoundary : ErrorBoundary
    {
        private readonly Stopwatch _stopwatch = new();
        private const int _frequentErrorTimeoutMs = 1000;

        [Inject]
        public IToastProvider ToastProvider { get; set; } = default!;

        [CascadingParameter]
        public IMainLayout? MainLayout { get; set; }

        protected override Task OnErrorAsync(Exception exception)
        {
            if (!_stopwatch.IsRunning || _stopwatch.ElapsedMilliseconds > _frequentErrorTimeoutMs)
                _ = RecoverDelayed();
            else
            {
                // wont recover
            }
            _stopwatch.Restart();
            ToastProvider.Show(new ToastMessage
            {
                Silent = true,
                Type = ToastMessageType.Error,
                HeaderText = "Unhandled UI error",
                BodyText = $"UI error occurred: {exception.Message}\nClick to reload the page.",
                TargetUri = MainLayout!.GetReloadUri(true),
                TargetUriForceReload = true,
                OnlyForLayoutOwner = MainLayout,
                Key = this,
                Exception = exception,
            });
            return Task.CompletedTask;
        }

        private async Task RecoverDelayed()
        {
            await Task.Yield();
            Recover();
        }
    }
}
