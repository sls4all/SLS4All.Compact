// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using SLS4All.Compact.Storage.PrinterSettings;

namespace SLS4All.Compact.McuClient
{

    public class McuFirmwareUpdaterOptions : IOptionsItemEnable
    {
        public class SdCardOptions : McuManagerOptions.ManagerMcuSdCardSpi
        {
        }

        public class ShellCommandOptions : LoggedScriptRunnerOptions
        {
        }

        public class AliasOptions
        {
            public required string FirmwareFilename { get; set; }
            public SdCardOptions? SdCard { get; set; }
            public ShellCommandOptions? ShellCommand { get; set; }
        }

        /// <remarks>
        /// Since firmware update is potentially dangerous and platform dependent, leave it disabled by default, unlike other features
        /// </remarks>
        public bool IsEnabled { get; set; } = false;
        public Dictionary<string, AliasOptions> Aliases { get; set; } = new();
        public TimeSpan DeviceCloseGrace { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan UpdateTimeout { get; set; } = TimeSpan.FromMinutes(5);
    }

    /// <summary>
    /// Placeholder class for logger
    /// </summary>
    public class McuFirmwareUpdaterOutput
    {
    }

    public class McuFirmwareUpdater
    {
        private sealed class ScriptRunner : LoggedScriptRunner<McuFirmwareUpdaterOutput>
        {
            private readonly IOptionsMonitor<McuFirmwareUpdaterOptions.ShellCommandOptions> _options;
            private readonly IAppDataWriter _appDataWriter;

            public ScriptRunner(
                ILogger logger,
                ILogger<McuFirmwareUpdaterOutput> outputLogger,
                IOptionsMonitor<McuFirmwareUpdaterOptions.ShellCommandOptions> options,
                IAppDataWriter appDataWriter)
                : base(logger, outputLogger, options)
            {
                _options = options;
                _appDataWriter = appDataWriter;
            }

            public Task<int?> Run(IMcuDevice device, string firmwareFilename, CancellationToken cancel)
            {
                cancel.ThrowIfCancellationRequested();
                var options = _options.CurrentValue;

                string ReplaceArgs(string text)
                    => text
                        .Replace("{{DeviceAlias}}", device.Info.Alias, StringComparison.OrdinalIgnoreCase)
                        .Replace("{{DeviceName}}", device.Info.Name, StringComparison.OrdinalIgnoreCase)
                        .Replace("{{DeviceEndpoint}}", device.Info.Endpoint, StringComparison.OrdinalIgnoreCase)
                        .Replace("{{DeviceBaud}}", device.Info.Baud.ToString(), StringComparison.OrdinalIgnoreCase)
                        .Replace("{{FirmwareFile}}", Path.GetFullPath(firmwareFilename), StringComparison.OrdinalIgnoreCase);

                var optionsDir = _appDataWriter.GetPublicOptionsDirectory();
                var filename = Path.Combine(optionsDir, options.ExecutablePlatform);
                if (!Path.Exists(filename))
                {
                    filename = options.ExecutablePlatform;
                    if (!Path.Exists(filename))
                        throw new InvalidOperationException($"Missing executable for update: {filename} ({Path.GetFullPath(filename)})");
                }
                var args = ReplaceArgs(options.ArgsPlatform);

                return Run(filename, args, cancel);
            }
        }


        private readonly ILogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IOptionsMonitor<McuFirmwareUpdaterOptions> _options;
        private readonly IMcu _mcu;
        private readonly IAppDataWriter _appDataWriter;

        public AsyncEvent PreUpdateEvent { get; } = new();

        public McuFirmwareUpdater(
            ILoggerFactory loggerFactory,
            IOptionsMonitor<McuFirmwareUpdaterOptions> options,
            IAppDataWriter appDataWriter,
            IMcu mcu)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<McuFirmwareUpdater>();
            _options = options;
            _appDataWriter = appDataWriter;
            _mcu = mcu;
        }

        public async Task CheckFirmwareUpdate(IMcuDevice device, CancellationToken cancel)
        {
            cancel.ThrowIfCancellationRequested();

            var options = _options.CurrentValue;
            if (!options.Aliases.TryGetValue(device.Info.Alias, out var alias))
                return;

            var firmwareFilename = alias.FirmwareFilename;
            var firmwareMetadataFilename = firmwareFilename + ".json";
            if (!File.Exists(firmwareFilename) || !File.Exists(firmwareMetadataFilename))
                return;
            McuFirmwareMetadata firmwareMetadata;
            using (var stream = File.OpenRead(firmwareMetadataFilename))
            {
                firmwareMetadata = await JsonSerializer.DeserializeAsync<McuFirmwareMetadata>(stream, cancellationToken: cancel)
                    ?? throw new FormatException($"Invalid firmware metadata for MCU {_mcu}");
            }

            if (firmwareMetadata.Version == _mcu.Config.Version)
            {
                _logger.LogInformation($"MCU {_mcu} reports firmware version {_mcu.Config.Version} and is up to date");
                return;
            }
            if (!options.IsEnabled)
            {
                _logger.LogInformation($"MCU {_mcu} reports old firmware version {_mcu.Config.Version} instead of current {firmwareMetadata.Version} but firmware update is disabled");
                return;
            }
            _logger.LogInformation($"MCU {_mcu} reports firmware version {_mcu.Config.Version} and current is {firmwareMetadata.Version}, will update");

            // update
            if (alias.SdCard != null)
                await UploadFirmwareSdCard(alias.SdCard, device, firmwareFilename, cancel);
            else if (alias.ShellCommand != null)
                await UploadFirmwareShellCommand(alias.ShellCommand, device, firmwareFilename, cancel);
        }

        private async Task UploadFirmwareShellCommand(
            McuFirmwareUpdaterOptions.ShellCommandOptions command,
            IMcuDevice device, 
            string firmwareFilename, 
            CancellationToken cancel)
        {
            var options = _options.CurrentValue;

            _logger.LogInformation($"Will update MCU {_mcu} firmware with shell command for firmware: {firmwareFilename}");

            await RunProtectedUpdate(device, async cancel =>
            {
                var runner = new ScriptRunner(
                    _logger,
                    _loggerFactory.CreateLogger<McuFirmwareUpdaterOutput>(),
                    TransformOptionsMonitor.Create(_options, options =>
                    {
                        if (options.Aliases.TryGetValue(device.Info.Alias, out var alias) && alias.ShellCommand != null)
                            return alias.ShellCommand;
                        else
                            return new();
                    }),
                    _appDataWriter);
                var exitCode = await runner.Run(device, firmwareFilename, cancel);
                if (exitCode == 0)
                {
                    _logger.LogWarning($"Firmware update for MCU {_mcu} succeeded");
                }
                else
                    throw new ApplicationException($"Firmware update for MCU {_mcu} failed with exit code '{exitCode}'");
            }, cancel);
        }

        private async Task UploadFirmwareSdCard(
            McuFirmwareUpdaterOptions.SdCardOptions sdOptions, 
            IMcuDevice device, 
            string firmwareFilename, 
            CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            _logger.LogInformation($"Will update MCU {_mcu} firmware with SdCard image made for firmware: {firmwareFilename}");

            var factory = device.Factory;
            var info = device.Info;

            await RunProtectedUpdate(device, async protectedCancel =>
            {
                using (var source = File.OpenRead(firmwareFilename))
                using (var image = FatFirmwareImageBuilder.CreateImage(source))
                {
                    // NOTE: mage a local McuManager that is unaffected by external cancellation and application shutdown
                    const string sdCardName = "sdCard";
                    var manager = new McuManagerLocal(
                        _loggerFactory,
                        ConstantOptionsMonitor.Create(new McuManagerOptions
                        {
                            Mcus =
                            {
                                { info.Name, new()
                                    {
                                        Device = new()
                                        {
                                            Path = McuAliasesOptions.FormatDeviceName(info.Endpoint, info.Baud),
                                        },
                                    } 
                                }
                            },
                            SdCardSpi =
                            {
                                { sdCardName, sdOptions }
                            }
                        }),
                        _appDataWriter,
                        [ factory ],
                        NullPrinterSettingsStorage.Instance);

                    using (var managerCancelSource = CancellationTokenSource.CreateLinkedTokenSource(protectedCancel))
                    {
                        var managerCancel = managerCancelSource.Token;
                        var runTask = manager.Run(managerCancel, cancel);
                        var finishedTask = await Task.WhenAny(manager.HasStartedTask, runTask);
                        await finishedTask;
                        if (finishedTask != manager.HasStartedTask)
                            throw new ApplicationException("Failed to start update manager");
                        var sdCard = manager.SdCards[sdCardName];
                        await sdCard.WriteSectors(
                            0,
                            (int)((image.Length + sdCard.SectorSize - 1) / sdCard.SectorSize),
                            image,
                            managerCancel);
                        managerCancelSource.Cancel();
                        await runTask;
                        // wait a bit for any continuations to finish after cancellation
                        await Task.Delay(options.DeviceCloseGrace, protectedCancel);
                    }
                    _logger.LogInformation($"Firmware update for MCU {_mcu} succeeded");
                }
            }, cancel);
        }

        private async Task RunProtectedUpdate(IMcuDevice device, Func<CancellationToken, Task> func, CancellationToken cancel)
        {
            await PreUpdateEvent.Invoke(cancel);

            var options = _options.CurrentValue;
            device.Dispose();
            await Task.Delay(options.DeviceCloseGrace, cancel);

            try
            {
                var cancelSource = new CancellationTokenSource(options.UpdateTimeout);
                using (var scheduler = new PriorityScheduler(
                    nameof(McuFirmwareUpdater),
                    ThreadPriority.Normal,
                    Environment.ProcessorCount,
                    isBackground: false /* important! */))
                {
                    var cancel2 = cancelSource.Token;
                    var updateTask = Task.Factory.StartNew(
                        _ => func(cancel2),
                        null,
                        cancel2,
                        TaskCreationOptions.None,
                        scheduler).Unwrap();
                    while (true)
                    {
                        cancel2.ThrowIfCancellationRequested();
                        var delay = Task.Delay(5000, cancel2);
                        await Task.WhenAny(updateTask, delay);
                        if (updateTask.IsCompleted)
                        {
                            await updateTask;
                            break;
                        }
                        _logger.LogWarning($"Still updating firmware for MCU {_mcu}!");
                    }

                }
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "Firmware update failed");
            }

            throw new McuAutomatedRestartException($"Reset needed after MCU {_mcu} firmware update", reason: McuAutomatedRestartReason.FirmwareUpdate);
        }
    }
}
