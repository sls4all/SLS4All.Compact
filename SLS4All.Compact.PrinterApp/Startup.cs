// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;
using SLS4All.Compact.DependencyInjection;
using SLS4All.Compact.Camera;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.IO;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Network;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.McuClient.Devices;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Storage.PrintSessions;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Temperature.SoftHeater;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Caching;
using SLS4All.Compact.McuClient.PipedMcu;
using SLS4All.Compact.Printing;
using NReco.Logging.File;
using SLS4All.Compact.Numerics;
using SLS4All.Compact.Storage.PrintProfiles;

namespace SLS4All.Compact
{
    public class Startup : StartupBase
    {
        public Startup(IConfiguration configuration) : base(configuration)
        {
        }

        protected override void ConfigureServicesProxy(IServiceProvider bootServiceProvider, ApplicationOptions applicationOptions, IServiceCollection services)
        {
            base.ConfigureServicesProxy(bootServiceProvider, applicationOptions, services);
            services.AddAsImplementationAndInterfaces<McuProxy>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<McuProxyComponent>(ServiceLifetime.Singleton);
            services.Configure<McuHostRunnerOptions>(Configuration.GetSection("McuHostRunner"));
            services.AddAsImplementationAndInterfaces<McuHostRunner>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<UserProfileAppDataWriter>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<McuProxyComponent>(ServiceLifetime.Singleton);
        }

        protected override async void ConfigureProxy(
            IServiceProvider services, 
            IHostApplicationLifetime lifetime, 
            ILogger<StartupBase> logger, 
            IOptions<ApplicationOptions> options)
        {
            base.ConfigureProxy(services, lifetime, logger, options);

            lifetime.ApplicationStarted.Register(() =>
            {
                LogStartup(logger, null);
            });

            var proxy = services.GetRequiredService<McuProxyComponent>();
            try
            {
                await proxy.Run();
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Unhandled exception");
            }
            finally
            {
                lifetime.StopApplication();
            }
        }

        protected override void SetupApp(IServiceProvider bootServiceProvider, ApplicationOptions applicationOptions, IServiceCollection services)
        {
            base.SetupApp(bootServiceProvider, applicationOptions, services);
            services.AddRazorPages();
            services.AddServerSideBlazor();
        }

        protected override IServiceProvider GetBootServiceProvider(IServiceCollection services)
        {
            services.AddOptions();
            services.Configure<ApplicationOptions>(Configuration.GetSection(ApplicationSection));
            services.Configure<UserProfileAppDataWriterOptions>(Configuration.GetSection(AppDataWriterSection));
            RegisterOptionsWriters(services, typeof(UserProfileAppDataOptionsWriter<>));
            services.AddAsImplementationAndInterfaces<UserProfileAppDataWriter>(ServiceLifetime.Singleton);
            return services.BuildServiceProvider();
        }

        protected override void ConfigureLogging(ApplicationOptions applicationOptions, IServiceCollection services)
        {
            base.ConfigureLogging(applicationOptions, services);
            services.AddLogging(loggingBuilder =>
            {
                var loggingSection = Configuration.GetSection("Logging");
                loggingBuilder.AddFile(loggingSection);
                loggingBuilder.AddConsole(options => options.FormatterName = CompactSimpleConsoleFormatter.FormatterName)
                    .AddConsoleFormatter<CompactSimpleConsoleFormatter, SimpleConsoleFormatterOptions>();
                var telegram = TelegramLoggerProviderExtensions.TryCreateTelegramProvider(loggingSection);
                if (telegram != null)
                    loggingBuilder.AddProvider(telegram);
            });
        }

        protected override void ConfigurePrinter(ApplicationOptions applicationOptions, IServiceCollection services)
        {
            base.ConfigurePrinter(applicationOptions, services);

            services.Configure<McuAliasesOptions>(Configuration.GetSection("McuAliases"));
            switch (applicationOptions.McuSerialDeviceFactory)
            {
                case McuSerialDeviceFactoryType.LocalSerial:
                    services.Configure<McuLocalSerialDeviceFactoryOptions>(Configuration.GetSection("McuLocalSerialDeviceFactory"));
                    services.AddAsImplementationAndInterfaces<McuLocalSerialDeviceFactory>(ServiceLifetime.Singleton);
                    break;
                case McuSerialDeviceFactoryType.ProxySerial:
                    services.Configure<McuProxySerialDeviceFactoryOptions>(Configuration.GetSection("McuProxySerialDeviceFactory"));
                    services.AddAsImplementationAndInterfaces<McuProxySerialDeviceFactory>(ServiceLifetime.Singleton);
                    break;
                case McuSerialDeviceFactoryType.SshSerial:
                    services.Configure<McuSshSerialDeviceFactoryOptions>(Configuration.GetSection("McuSshSerialDeviceFactory"));
                    services.AddAsImplementationAndInterfaces<McuSshSerialDeviceFactory>(ServiceLifetime.Singleton);
                    break;
            }

            switch (applicationOptions.PrinterClient)
            {
                case PrinterClientType.Fake:
                    services.Configure<FakePrinterClientOptions>(Configuration.GetSection("FakePrinterClient"));
                    services.AddAsImplementationAndInterfaces<FakePrinterClient>(ServiceLifetime.Singleton);
                    break;
                case PrinterClientType.McuManagerLocal:
                case PrinterClientType.PipedMcuManagerProxy:
                    services.Configure<McuStepperGlobalOptions>(Configuration.GetSection("McuStepperGlobal"));
                    services.Configure<McuManagerOptions>(Configuration.GetSection("McuManager"));
                    services.Configure<McuHostRunnerOptions>(Configuration.GetSection("McuHostRunner"));
                    services.AddAsImplementationAndInterfaces<McuHostRunner>(ServiceLifetime.Singleton);
                    services.Configure<McuPrinterClientOptions>(Configuration.GetSection("McuPrinterClient"));
                    services.AddAsImplementationAndInterfaces<McuPrinterClient>(ServiceLifetime.Singleton);
                    break;
            }

            switch (applicationOptions.PrinterClient)
            {
                case PrinterClientType.McuManagerLocal:
                    services.AddAsImplementationAndParents<McuManagerLocal>(ServiceLifetime.Singleton);
                    break;
                case PrinterClientType.PipedMcuManagerProxy:
                    services.AddAsImplementationAndParents<PipedMcuManagerProxy>(ServiceLifetime.Singleton);
                    services.Configure<PipedMcuProxyRunnerOptions>(Configuration.GetSection("PipedMcuProxyRunner"));
                    services.AddAsImplementationAndParents<PipedMcuProxyRunner>(ServiceLifetime.Singleton);
                    break;
            }

            switch (applicationOptions.Plotter)
            {
                case PrinterPloterType.Fake:
                    services.AddAsImplementationAndInterfaces<NullCodePlotter>(ServiceLifetime.Singleton);
                    break;
                case PrinterPloterType.Image:
                    services.Configure<ImageCodePlotterOptions>(Configuration.GetSection("ImageCodePlotter"));
                    services.AddAsImplementationAndInterfaces<ImageCodePlotter>(ServiceLifetime.Singleton);
                    break;
            }

            switch (applicationOptions.PrinterClient)
            {
                case PrinterClientType.Fake:
                    services.Configure<FakeMovementClientOptions>(Configuration.GetSection("FakeMovementClient"));
                    services.AddAsImplementationAndInterfaces<FakeMovementClient>(ServiceLifetime.Singleton);
                    services.Configure<FakeTemperatureClientOptions>(Configuration.GetSection("FakeTemperatureClient"));
                    services.AddAsImplementationAndInterfaces<FakeTemperatureClient>(ServiceLifetime.Singleton);
                    break;
                case PrinterClientType.PipedMcuManagerProxy:
                case PrinterClientType.McuManagerLocal:
                    services.Configure<McuMovementClientOptions>(Configuration.GetSection("McuMovementClient"));
                    services.AddAsImplementationAndInterfaces<McuMovementClient>(ServiceLifetime.Singleton);
                    services.Configure<McuTemperatureClientOptions>(Configuration.GetSection("McuTemperatureClient"));
                    services.AddAsImplementationAndInterfaces<McuTemperatureClient>(ServiceLifetime.Singleton);
                    break;
            }

            services.Configure<PrinterShutdownMonitorOptions>(Configuration.GetSection("PrinterShutdownMonitor"));
            services.AddAsImplementationAndInterfaces<PrinterShutdownMonitor>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<XYHomingInitializer>(ServiceLifetime.Singleton);

            switch (applicationOptions.PrinterClient)
            {
                case PrinterClientType.Fake:
                    services.AddAsImplementationAndInterfaces<NullPowerClient>(ServiceLifetime.Singleton);
                    services.AddAsImplementationAndInterfaces<NullInputClient>(ServiceLifetime.Singleton);
                    break;
                case PrinterClientType.PipedMcuManagerProxy:
                case PrinterClientType.McuManagerLocal:
                    services.Configure<McuPowerClientOptions>(Configuration.GetSection("McuPowerClient"));
                    services.AddAsImplementationAndInterfaces<McuPowerClient>(ServiceLifetime.Singleton);
                    services.Configure<McuInputClientOptions>(Configuration.GetSection("McuInputClient"));
                    services.AddAsImplementationAndInterfaces<McuInputClient>(ServiceLifetime.Singleton);
                    break;
            }

            switch (applicationOptions.TemperatureCameraClient)
            {
                case TemperatureCameraClientType.Mlx90640Fake:
                    services.Configure<Mlx90640CameraOptions>(Configuration.GetSection("Mlx90640Camera"));
                    services.AddAsImplementationAndInterfaces<Mlx90640FakeCamera>(ServiceLifetime.Singleton);
                    break;
                case TemperatureCameraClientType.Mlx90640Local:
                    services.Configure<Mlx90640CameraOptions>(Configuration.GetSection("Mlx90640Camera"));
                    services.AddAsImplementationAndInterfaces<Mlx90640Camera>(ServiceLifetime.Singleton);
                    break;
            }

            services.Configure<DefaultTemperatureHistoryOptions>(Configuration.GetSection("DefaultTemperatureHistory"));
            services.AddAsImplementationAndInterfaces<DefaultTemperatureHistory>(ServiceLifetime.Singleton);
            services.Configure<DefaultTemperatureLoggerOptions>(Configuration.GetSection("DefaultTemperatureLogger"));
            services.AddAsImplementationAndInterfaces<DefaultTemperatureLogger>(ServiceLifetime.Singleton);
            services.Configure<AnalyseHeatingOptions>(Configuration.GetSection("AnalyseHeating"));
            services.AddAsImplementationAndInterfaces<AnalyseHeating>(ServiceLifetime.Singleton);
            services.Configure<MeasureHeatingOptions>(Configuration.GetSection("MeasureHeating"));
            services.AddAsImplementationAndInterfaces<MeasureHeating>(ServiceLifetime.Singleton);

            switch (applicationOptions.PrinterClient)
            {
                case PrinterClientType.Fake:
                    services.AddAsImplementationAndInterfaces<NullHalogenClient>(ServiceLifetime.Singleton);
                    break;
                case PrinterClientType.PipedMcuManagerProxy:
                case PrinterClientType.McuManagerLocal:
                    services.Configure<McuHalogenClientOptions>(Configuration.GetSection("McuHalogenClient"));
                    services.AddAsImplementationAndInterfaces<McuHalogenClient>(ServiceLifetime.Singleton);
                    break;
            }

            services.Configure<HalogenHeaterCheckerOptions>(Configuration.GetSection("HalogenHeaterChecker"));
            services.AddAsImplementationAndInterfaces<HalogenHeaterChecker>(ServiceLifetime.Transient);

            services.Configure<ChamberHeaterCheckerOptions>(Configuration.GetSection("ChamberHeaterChecker"));
            services.AddAsImplementationAndInterfaces<ChamberHeaterChecker>(ServiceLifetime.Transient);

            switch (applicationOptions.SoftHeater)
            {
                case SoftHeaterType.SoftAnalysis:
                    services.Configure<SoftAnalysisSurfaceHeaterOptions>(Configuration.GetSection("SoftAnalysisSurfaceHeater"));
                    services.AddAsImplementationAndInterfaces<SoftAnalysisSurfaceHeater>(ServiceLifetime.Singleton);
                    break;
            }
            services.Configure<ControlledHeatingOptions>(Configuration.GetSection("ControlledHeating"));
            services.AddAsImplementationAndInterfaces<ControlledHeating>(ServiceLifetime.Singleton);

            switch (applicationOptions.VideoCameraClient)
            {
                case VideoCameraClientType.Fake:
                    services.Configure<FakeCameraClientOptions>(Configuration.GetSection("FakeCamera"));
                    services.AddAsImplementationAndInterfaces<FakeCameraClient>(ServiceLifetime.Singleton);
                    break;
                case VideoCameraClientType.Mjpeg:
                    services.Configure<MjpegDeviceCameraClientOptions>(Configuration.GetSection("MjpegDeviceCamera"));
                    services.AddAsImplementationAndInterfaces<MjpegDeviceCameraClient>(ServiceLifetime.Singleton);
                    break;
                case VideoCameraClientType.V4L2Grayscale:
                    services.Configure<V4L2GrayscaleCameraClientOptions>(Configuration.GetSection("V4L2GrayscaleCamera"));
                    services.AddAsImplementationAndInterfaces<V4L2GrayscaleCameraClient>(ServiceLifetime.Singleton);
                    break;
            }

            // NOTE: code history is not currentlt used and consumes resources
            //services.Configure<DefaultGCodeHistoryOptions>(Configuration.GetSection("DefaultGCodeHistory"));
            //services.AddAsImplementationAndInterfaces<DefaultGCodeHistory>(ServiceLifetime.Singleton);

            services.Configure<LayerClientOptions>(Configuration.GetSection("LayerClient"));
            services.AddAsImplementationAndInterfaces<LayerClient>(ServiceLifetime.Singleton);

            switch (applicationOptions.NetworkManager)
            {
                case NetworkManagerType.Fake:
                    services.Configure<FakeNetworkManagerOptions>(Configuration.GetSection("FakeNetworkManager"));
                    services.AddAsImplementationAndInterfaces<FakeNetworkManager>(ServiceLifetime.Singleton);
                    break;
                case NetworkManagerType.DBus:
                    services.Configure<DBusNetworkManagerOptions>(Configuration.GetSection("DBusNetworkManager"));
                    services.AddAsImplementationAndInterfaces<DBusNetworkManager>(ServiceLifetime.Singleton);
                    break;
            }

            services.AddAsImplementationAndInterfaces<PrinterSettingsInitializer>(ServiceLifetime.Singleton);

            services.Configure<PowerBuzzerClientOptions>(Configuration.GetSection("PowerBuzzerClient"));
            services.AddAsImplementationAndInterfaces<PowerBuzzerClient>(ServiceLifetime.Singleton);

            services.Configure<BuzzerMelodyClientOptions>(Configuration.GetSection("BuzzerMelodyClient"));
            services.AddAsImplementationAndInterfaces<BuzzerMelodyClient>(ServiceLifetime.Singleton);

            services.Configure<GalvoFanMonitorOptions>(Configuration.GetSection("GalvoFanMonitor"));
            services.AddAsImplementationAndInterfaces<GalvoFanMonitor>(ServiceLifetime.Singleton);

            services.Configure<FirmwareConnectedNotifierOptions>(Configuration.GetSection("FirmwareConnectedNotifier"));
            services.AddAsImplementationAndInterfaces<FirmwareConnectedNotifier>(ServiceLifetime.Singleton);

            services.Configure<PrinterDataBackupManagerOptions>(Configuration.GetSection("PrinterDataBackupManager"));
            services.AddAsImplementationAndInterfaces<PrinterDataBackupManager>(ServiceLifetime.Singleton);

            services.Configure<PrinterWearCaptureOptions>(Configuration.GetSection("PrinterWearCapture"));
            services.AddAsImplementationAndInterfaces<PrinterWearCapture>(ServiceLifetime.Singleton);
            services.Configure<PrinterMaintenanceManagerOptions>(Configuration.GetSection("PrinterMaintenanceManager"));
            services.AddAsImplementationAndInterfaces<PrinterMaintenanceManager>(ServiceLifetime.Singleton);

            services.AddAsImplementationAndInterfaces<FileSystemPrintSessionStorage>(ServiceLifetime.Singleton);

            services.Configure<PrinterPerformanceProviderOptions>(Configuration.GetSection("PrinterPerformanceProvider"));
            services.AddAsImplementationAndInterfaces<PrinterPerformanceProvider>(ServiceLifetime.Singleton);

            services.AddAsImplementationAndInterfaces<CombinedClipboardProvider>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<DefaultUnitConverter>(ServiceLifetime.Singleton);

            services.Configure<FileSystemTempBlobStorageOptions>(Configuration.GetSection("FileSystemTempBlobStorage"));
            services.AddAsImplementationAndInterfaces<FileSystemTempBlobStorage>(ServiceLifetime.Singleton);

            services.Configure<DefaultPrintProfileInitializerOptions>(Configuration.GetSection("DefaultPrintProfileInitializer"));
            services.AddAsImplementationAndInterfaces<DefaultPrintProfileInitializer>(ServiceLifetime.Transient);

            services.Configure<PrintingServiceOptions>(Configuration.GetSection("PrintingService"));
            services.AddAsImplementationAndInterfaces<PrintingServiceScoped>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<PrintingService>(ServiceLifetime.Singleton);
            services.Configure<DefaultLayerEstimateExtrapolatorOptions>(Configuration.GetSection("DefaultLayerEstimateExtrapolator"));
            services.AddAsImplementationAndInterfaces<DefaultLayerEstimateExtrapolator>(ServiceLifetime.Transient);
            services.AddSingleton(provider => new Func<ILayerEstimateExtrapolator>(() => provider.GetRequiredService<ILayerEstimateExtrapolator>()));

            RegisterPluginOptions(applicationOptions, services);
            RegisterPluginServices(applicationOptions, services);

            // NOTE: MUST start and register LAST!
            services.Configure<PrinterWatchDogMonitorOptions>(Configuration.GetSection("WatchDogMonitor"));
            services.AddAsImplementationAndInterfaces<PrinterWatchDogMonitor>(ServiceLifetime.Singleton);
        }
    }
}
