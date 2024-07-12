// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text;
using SLS4All.Compact.Pages;
using SLS4All.Compact.Printer;
using Microsoft.Extensions.Logging;

namespace SLS4All.Compact.ComponentModel
{
    public class PrinterLifetimeOptions
    {
        public string? CommandOutputFilename { get; set; }
    }

    public sealed class PrinterLifetime : Printer.IPrinterLifetime
    {
        private readonly ILogger<PrinterLifetime> _logger;
        private readonly IOptions<PrinterLifetimeOptions> _options;
        private readonly IToastProvider _toastProvider;
        private readonly IHostApplicationLifetime _hostLifetime;
        private volatile bool _isStopping;

        public PrinterLifetimeRequest? LastRequest { get; set; }
        public bool IsStopping => _isStopping;

        public PrinterLifetime(
            ILogger<PrinterLifetime> logger,
            IOptions<PrinterLifetimeOptions> options,
            IToastProvider toastProvider,
            IHostApplicationLifetime hostLifetime)
        {
            _logger = logger;
            _options = options;
            _toastProvider = toastProvider;
            _hostLifetime = hostLifetime;
            DeleteCommandFile();
        }

        private void DeleteCommandFile()
        {
            var options = _options.Value;
            try
            {
                if (!string.IsNullOrEmpty(options.CommandOutputFilename) && File.Exists(options.CommandOutputFilename))
                    File.Delete(options.CommandOutputFilename);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete command file");
            }
        }

        private void WriteCommandFile(PrinterShutdownMode mode)
        {
            var options = _options.Value;
            try
            {
                if (!string.IsNullOrEmpty(options.CommandOutputFilename))
                {
                    File.WriteAllText(options.CommandOutputFilename, mode switch
                    {
                        PrinterShutdownMode.UpdateApplication => "update",
                        PrinterShutdownMode.ExitApplication => "exit",
                        PrinterShutdownMode.RebootSystem => "reboot",
                        PrinterShutdownMode.ShutdownSystem => "shutdown",
                        PrinterShutdownMode.RestartApplication => "restart",
                        _ => "",
                    }, Encoding.ASCII);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to write command file");
            }
        }

        public Task RequestShutdown(PrinterShutdownMode mode, Func<Task>? callback)
        {
            LastRequest = new PrinterLifetimeRequest(mode, callback);
            foreach (var page in AppPage.ActivePages)
            {
                try
                {
                    page.NavigationManager?.NavigateTo(ShutdownPage.SelfPath);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to navigate to shutdown for listed active page");
                }
            }
            return Task.CompletedTask;
        }

        public async Task PerformShutdown(PrinterLifetimeRequest request)
        {
            try
            {
                _logger.LogWarning($"Performing {request.Mode} as requested!");
                _isStopping = true;
                if (request.Callback != null)
                    await request.Callback.Invoke();
                foreach (var page in AppPage.ActivePages)
                {
                    try
                    {
                        var layout = page.MainLayout;
                        if (layout != null)
                            await layout.SetPageLoader();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, $"Failed to set page loader for listed active page");
                    }
                }
                WriteCommandFile(request.Mode);
                _hostLifetime.StopApplication();
            }
            catch (Exception ex)
            {
                _isStopping = false;
                _toastProvider.Show(new ToastMessage
                {
                    Type = ToastMessageType.Error,
                    HeaderText = "Failed to shutdown",
                    BodyText = ex.Message,
                    Exception = ex,
                });
            }
        }
    }
}
