// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using MediatR;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;
using System.Globalization;
using System.Threading.Tasks;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Slicing;
using Microsoft.Extensions.Logging;
using System.Reflection;
using System.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Linq;
using SLS4All.Compact.DependencyInjection;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.PrinterSettings;
using SLS4All.Compact.Printer;
using SLS4All.Compact.UpdateModel;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Temperature;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.IO;
using SLS4All.Compact.Storage.PrintJobs;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Network;
using SLS4All.Compact.Power;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Storage.PrintProfiles;
using SLS4All.Compact.Diagnostics;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.FileProviders;
using System.Security.Cryptography.Xml;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.AspNetCore.Http;
using SLS4All.Compact.Processing.Slicing;
using SLS4All.Compact.Validation;
using System.Net.WebSockets;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using SLS4All.Compact.Pages.Wizards;

namespace SLS4All.Compact
{
    public abstract class StartupBase
    {
        public static HashSet<string> NoCacheExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".css",
            ".js",
        };
        public const string ApplicationSection = "Application";
        public const string AppDataWriterSection = "UserProfileAppDataWriter";
        public static HashSet<Type> OptionsWriterTypes { get; } =
        [
            typeof(PrinterSettingsStorageSavedOptions),
            typeof(ApplicationDeploymentSavedOptions),
            typeof(DefaultPrinterCultureManagerSavedOptions),
            typeof(PrinterTimeManagerSavedOptions),
            typeof(AppThemeManagerSavedOptions),
            typeof(CompactMemberManagerSavedOptions),
            typeof(SystemPrinterAuthenticationSavedOptions),
            typeof(PrinterWearCaptureSavedOptions),
            typeof(PrinterMaintenanceManagerSavedOptions),
            typeof(InovaAdvancedBedProjectionSavedOptions),
        ];
        public static string[] ConfigurationSources { get; set; } = [];
        public static bool AppsettingsSafeModeEnabled { get; set; }
        public static Exception? AppsettingsSafeModeException { get; set; }

        public StartupBase(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        protected void RegisterOptionsWriters(IServiceCollection services, Type openType)
        {
            foreach (var type in OptionsWriterTypes)
            {
                var configureType = typeof(OptionsConfigurationServiceCollectionExtensions)
                    .GetMethods(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
                    .Single(x => x.Name == nameof(OptionsConfigurationServiceCollectionExtensions.Configure) && x.GetParameters().Length == 2)
                    .MakeGenericMethod(type);
                configureType.Invoke(
                    null,
                    new object[] { services, Configuration.GetSection(UserProfileAppDataWriter.GetOptionsSectionName(type)) });
                services.AddAsImplementationAndInterfaces(openType.MakeGenericType(type), ServiceLifetime.Singleton);
            }
        }

        protected virtual void ConfigureLogging(ApplicationOptions applicationOptions, IServiceCollection services)
        {
        }

        protected virtual void ConfigurePrinter(ApplicationOptions applicationOptions, IServiceCollection services)
        {
        }

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            // application config
            var bootServiceProvider = GetBootServiceProvider(services);
            var applicationOptions = bootServiceProvider.GetRequiredService<IOptions<ApplicationOptions>>().Value;

            // logging
            ConfigureLogging(applicationOptions, services);

            if (applicationOptions.Proxy)
                ConfigureServicesProxy(bootServiceProvider, applicationOptions, services);
            else
                ConfigureServicesApp(bootServiceProvider, applicationOptions, services);
        }

        protected virtual void ConfigureServicesProxy(IServiceProvider bootServiceProvider, ApplicationOptions applicationOptions, IServiceCollection services)
        {
        }

        protected virtual void ConfigureServicesApp(IServiceProvider bootServiceProvider, ApplicationOptions applicationOptions, IServiceCollection services)
        { 
            SetupApp(bootServiceProvider, applicationOptions, services);

            LoadPluginAssemblies(applicationOptions);
            LoadPluginReplacements(applicationOptions);

            services.AddMediatR(builder =>
            {
                builder.RegisterServicesFromAssembly(typeof(StartupBase).Assembly);
            });
            services.AddHttpClient();
            services.AddControllers().AddApplicationPart(typeof(StartupBase).Assembly);

            // storage
            services.Configure<FileSystemPrintProfileStorageOptions>(Configuration.GetSection("FileSystemPrintProfileStorage"));
            services.AddAsImplementationAndInterfaces<FileSystemPrintProfileStorage>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<OptionsPrinterSettingsStorage>(ServiceLifetime.Singleton);

            // application
            ConfigurePrinter(applicationOptions, services);

            services.Configure<InovaAdvancedBedProjectionOptions>(Configuration.GetSection("InovaBedProjection"));
            services.AddAsImplementationAndInterfaces<InovaAdvancedBedProjection>(ServiceLifetime.Singleton);

            services.Configure<FrontendOptions>(Configuration.GetSection("Frontend"));

            services.AddAsImplementationAndInterfaces<Pages.Test2D.ValuesContainer>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<Pages.SlicingPage.ValuesContainer>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<Pages.MovementPage.ValuesContainer>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<Components.TemperatureControl.ValuesContainer>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<Components.ThermoCameraView.ValuesContainer>(ServiceLifetime.Scoped);
            services.Configure<Pages.ThermoCameraCompare.ThermoCameraCompareOptions>(Configuration.GetSection("ThermoCameraCompare"));

            services.Configure<ExhaustiveNesterOptions>(Configuration.GetSection("ExhaustiveNester"));
            services.AddAsImplementationAndInterfaces<ExhaustiveNesterLocalWorkerProvider>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<ExhaustiveNester>(ServiceLifetime.Transient);

            services.Configure<NestingServiceOptions>(Configuration.GetSection("NestingService"));
            services.AddAsImplementationAndInterfaces<NestingServiceScoped>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<NestingService>(ServiceLifetime.Singleton);
            services.Configure<UIToastProviderOptions>(Configuration.GetSection("UIToastProvider"));
            services.AddAsImplementationAndInterfaces<UIToastProvider>(ServiceLifetime.Singleton);
            services.Configure<FileEdgeStorageOptions>(Configuration.GetSection("FileEdgeStorage"));
            services.AddAsImplementationAndInterfaces<FileEdgeStorage>(ServiceLifetime.Singleton);

            if (applicationOptions.UseIdealCircleHotspotCalculator)
            {
                services.Configure<BitmapSliceIdealCircleHotspotCalculatorOptions>(Configuration.GetSection("BitmapSliceIdealCircleHotspotCalculator"));
                services.AddAsImplementationAndParents<BitmapSliceIdealCircleHotspotCalculator>(ServiceLifetime.Transient);
            }
            else
            {
                services.Configure<BitmapSliceProjectionHotspotCalculatorOptions>(Configuration.GetSection("BitmapSliceProjectionHotspotCalculator"));
                services.AddAsImplementationAndInterfaces<BitmapSliceProjectionHotspotCalculatorInitializer>(ServiceLifetime.Singleton);
                services.AddAsImplementationAndParents<BitmapSliceProjectionHotspotCalculator>(ServiceLifetime.Transient);
            }
            services.Configure<BitmapSliceProcessorOptions>(Configuration.GetSection("BitmapSliceProcessor"));
            services.AddAsImplementationAndInterfaces<BitmapSliceProcessor>(ServiceLifetime.Transient);
            services.Configure<Controllers.BedMatrixControllerOptions>(Configuration.GetSection("BedMatrixController"));


            services.AddAsImplementationAndInterfaces<ValidationContextFactoryScoped>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<ValidationContextFactory>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<JobStorage>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<CurrentJobProvider>(ServiceLifetime.Scoped);
            services.AddAsImplementationAndInterfaces<CurrentPrintingParamsProvider>(ServiceLifetime.Singleton);

            services.Configure<FirstRenderExecutorOptions>(Configuration.GetSection("FirstRenderExecutor"));
            services.AddAsImplementationAndInterfaces<FirstRenderExecutor>(ServiceLifetime.Singleton);

            services.Configure<CompactUpdateCheckerOptions>(Configuration.GetSection("CompactUpdateChecker"));
            services.AddAsImplementationAndInterfaces<CompactUpdateChecker>(ServiceLifetime.Singleton);

            services.Configure<PrinterLifetimeOptions>(Configuration.GetSection("PrinterLifetime"));
            services.AddAsImplementationAndInterfaces<PrinterLifetime>(ServiceLifetime.Singleton);

            services.AddAsImplementationAndInterfaces<ScriptCreator>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<AppThemeManager>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<DefaultPrinterCultureManager>(ServiceLifetime.Singleton);

            services.Configure<PrinterTimeManagerOptions>(Configuration.GetSection("PrinterTimeManager"));
            services.AddAsImplementationAndInterfaces<PrinterTimeManager>(ServiceLifetime.Scoped);

            services.AddAsImplementationAndInterfaces<GuessingMeshLoader>(ServiceLifetime.Transient);

            services.Configure<SystemPrinterAuthenticationOptions>(Configuration.GetSection("SystemPrinterAuthentication"));
            services.AddAsImplementationAndInterfaces<SystemPrinterAuthentication>(ServiceLifetime.Singleton);
            services.AddAsImplementationAndInterfaces<CompactMemberManager>(ServiceLifetime.Singleton);

            services.Configure<PrinterFileProviderOptions>(Configuration.GetSection("PrinterFileProvider"));
            services.AddAsImplementationAndInterfaces<PrinterFileProvider>(ServiceLifetime.Singleton);

            services.Configure<EmergencyHelperOptions>(Configuration.GetSection("EmergencyHelper"));
            services.AddAsImplementationAndInterfaces<EmergencyHelper>(ServiceLifetime.Transient);

            services.AddAsImplementationAndInterfaces<LaserSafetySwitchMonitor>(ServiceLifetime.Singleton);

            services.Configure<SafeShutdownManagerOptions>(Configuration.GetSection("SafeShutdownManager"));
            services.AddAsImplementationAndInterfaces<SafeShutdownManager>(ServiceLifetime.Singleton);

            services.Configure<ImageStreamingHelperOptions>(Configuration.GetSection("ImageStreamingHelper"));
            services.AddAsImplementationAndInterfaces<ImageStreamingHelper>(ServiceLifetime.Singleton);
            
            services.Configure<OpticalSetupWizardOptions>(Configuration.GetSection("OpticalSetupWizard"));
            services.Configure<ThermoSetupWizardOptions>(Configuration.GetSection("ThermoSetupWizard"));
            services.Configure<GalvoCalibrationWizardOptions>(Configuration.GetSection("GalvoCalibrationWizard"));

            //services.AddAsImplementationAndInterfaces<BasicSlicerEdgeSorter>(ServiceLifetime.Transient);
            services.AddAsImplementationAndInterfaces<AdvancedSlicerEdgeSorter>(ServiceLifetime.Transient);
        }

        protected virtual void LoadPluginAssemblies(ApplicationOptions applicationOptions)
        {
            foreach (var assemblyPath in applicationOptions.PluginAssemblies.GetOrderedEnabledValues())
            {
                var assembly = Assembly.LoadFrom(assemblyPath);
                CompactServiceCollectionExtensions.ScanAssemblies.Add(assembly);
            }
        }

        protected virtual void LoadPluginReplacements(ApplicationOptions applicationOptions)
        {
            foreach ((var key, var replacememt) in applicationOptions.PluginReplacements.GetOrderedEnabledKeyValues())
            {
                if (string.IsNullOrWhiteSpace(replacememt.Original))
                    throw new InvalidOperationException($"Plugin replacement with key {key} does not have set original type");
                if (string.IsNullOrWhiteSpace(replacememt.Replacement))
                    throw new InvalidOperationException($"Plugin replacement with key {key} does not have set replacement type");
                var originalType = Type.GetType(replacememt.Original, true)!;
                var replacementType = Type.GetType(replacememt.Replacement, true)!;
                CompactServiceCollectionExtensions.PluginReplacements[originalType] = replacementType;
            }
        }

        protected virtual void RegisterPluginOptions(ApplicationOptions applicationOptions, IServiceCollection services)
        {
            foreach ((var key, var options) in applicationOptions.PluginOptions.GetOrderedEnabledKeyValues())
            {
                if (string.IsNullOrWhiteSpace(options.Options))
                    throw new InvalidOperationException($"Plugin options with key {key} does not have set options type");
                var optionsType = Type.GetType(options.Options, true)!;
                var configure = typeof(OptionsConfigurationServiceCollectionExtensions).GetMethods().Single(x =>
                {
                    if (x.Name != nameof(OptionsConfigurationServiceCollectionExtensions.Configure))
                        return false;
                        var parameters = x.GetParameters();
                        return parameters.Length == 3 &&
                            parameters[0].Name == "services" &&
                            parameters[1].Name == "name" &&
                            parameters[2].Name == "config";
                });
                var configureTyped = configure.MakeGenericMethod(optionsType);
                configureTyped.Invoke(null, [services, options.Name, Configuration.GetSection(options.Section)]);
            }
        }

        protected virtual void RegisterPluginServices(ApplicationOptions applicationOptions, IServiceCollection services)
        {
            foreach ((var key, var service) in applicationOptions.PluginServices.GetOrderedEnabledKeyValues())
            {
                if (string.IsNullOrWhiteSpace(service.Implementation))
                    throw new InvalidOperationException($"Plugin service with key {key} does not have set implementation type");
                var implementationType = Type.GetType(service.Implementation, true)!;
                switch (service.Registration)
                {
                    case ApplicationOptions.PluginServiceRegistration.AsImplementationAndInterfaces:
                        services.AddAsImplementationAndInterfaces(implementationType, service.Lifetime);
                        break;
                    case ApplicationOptions.PluginServiceRegistration.AsImplementationAndParents:
                        services.AddAsImplementationAndParents(implementationType, service.Lifetime);
                        break;
                    case ApplicationOptions.PluginServiceRegistration.AsImplementation:
                        services.AddAsImplementation(implementationType, service.Lifetime);
                        break;
                    case ApplicationOptions.PluginServiceRegistration.AsService:
                        if (string.IsNullOrWhiteSpace(service.Service))
                            throw new InvalidOperationException($"Plugin service with key {key} does not have set service type");
                        var serviceType = Type.GetType(service.Service, true)!;
                        services.AddAsService(serviceType, implementationType, service.Lifetime);
                        break;
                }
            }
        }

        protected virtual void SetupApp(IServiceProvider bootServiceProvider, ApplicationOptions applicationOptions, IServiceCollection services)
        {
        }

        protected abstract IServiceProvider GetBootServiceProvider(IServiceCollection services);

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public virtual void Configure(
            IServiceProvider services,
            IApplicationBuilder app,
            IHostApplicationLifetime lifetime,
            ILogger<StartupBase> logger,
            IOptions<ApplicationOptions> options)
        {
            var o = options.Value;
            if (o.Proxy)
                ConfigureProxy(services, lifetime, logger, options);
            else
                ConfigureApp(
                    logger,
                    options,
                    app,
                    lifetime,
                    services);
        }

        protected virtual void ConfigureProxy(IServiceProvider services,
            IHostApplicationLifetime lifetime,
            ILogger<StartupBase> logger,
            IOptions<ApplicationOptions> options)
        {
            var constructables = services.GetRequiredService<IEnumerable<IObjectFactory<IConstructable, object>>>();
            var delayedConstructables = services.GetRequiredService<IEnumerable<IObjectFactory<IDelayedConstructable, object>>>();
            InitializeConstructables(
                logger,
                services,
                constructables,
                delayedConstructables,
                lifetime.ApplicationStopping)
                .GetAwaiter().GetResult();
        }

        protected virtual void ConfigureApp(
            ILogger<StartupBase> logger,
            IOptions<ApplicationOptions> options,
            IApplicationBuilder app,
            IHostApplicationLifetime lifetime,
            IServiceProvider services)
        {
            var server = services.GetRequiredService<IServer>();
            var feOptions = services.GetRequiredService<IOptions<FrontendOptions>>();
            var printerCultureManager = services.GetRequiredService<IPrinterCultureManager>();
            var toastProvider = services.GetRequiredService<IToastProvider>();
            var constructables = services.GetRequiredService<IEnumerable<IObjectFactory<IConstructable, object>>>();
            var delayedConstructables = services.GetRequiredService<IEnumerable<IObjectFactory<IDelayedConstructable, object>>>();

            if (feOptions.Value.ShowAdvancedDebugFeatures)
            {
                app.UseDeveloperExceptionPage();
            }
            else
            {
                app.UseExceptionHandler("/Error");
            }

            // https://github.com/dotnet/aspnetcore/issues/27966
            app.UseStaticFiles(new StaticFileOptions
            {
                 OnPrepareResponse = (context) =>
                 {
                     SetCacheHeadersIfNecessary(context.Context);
                 },
            });

            app.UseRouting();

            app.UseRequestLocalization(parameters =>
            {
                var cultures = CultureInfo
                    .GetCultures(CultureTypes.AllCultures)
                    .Select(t => t.Name)
                    .ToArray();
                parameters.AddSupportedCultures(cultures)
                    .AddSupportedUICultures(cultures)
                    .SetDefaultCulture("en-US");
                parameters.RequestCultureProviders.Insert(0, new PrinterLocalRequestCultureProvider(printerCultureManager));
            });

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapBlazorHub();
                endpoints.MapFallbackToPage("/_Host");
            });

            // https://github.com/dotnet/aspnetcore/issues/27966
            app.Use((ctx, next) => {
                SetCacheHeadersIfNecessary(ctx);
                return next();
            });

            lifetime.ApplicationStarted.Register(() =>
            {
                LogStartup(logger, toastProvider);
                StartBrowser(server, options.Value);
            });

            InitializeConstructables(
                logger,
                services,
                constructables,
                delayedConstructables,
                lifetime.ApplicationStopping)
                .GetAwaiter().GetResult();
        }

        protected virtual async Task InitializeConstructables(
            ILogger logger,
            IServiceProvider services,
            IEnumerable<IObjectFactory<IConstructable, object>> constructables, 
            IEnumerable<IObjectFactory<IDelayedConstructable, object>> delayedConstructables, 
            CancellationToken cancel)
        {
            // initialize constructables
            try
            {
                logger.LogInformation($"Creating constructables");
                foreach (var factory in constructables)
                {
                    cancel.ThrowIfCancellationRequested();
                    using var obj = factory.CreateDisposable();
                    await obj.Instance.Construct(cancel);
                }
                logger.LogInformation($"Finished constructables");
            }
            catch (Exception ex)
            {
                if (!cancel.IsCancellationRequested)
                    logger.LogCritical(ex, $"Unhandled exception on constructables");
                throw;
            }

            // initialize delayed constructables
            _ = Task.Run(async () =>
            {
                try
                {
                    logger.LogInformation($"Creating delayed constructables");
                    foreach (var factory in delayedConstructables)
                    {
                        cancel.ThrowIfCancellationRequested();
                        using var obj = factory.CreateDisposable();
                        await obj.Instance.DelayedConstruct(cancel);
                    }
                    logger.LogInformation($"Finished delayed constructables");
                }
                catch (Exception ex)
                {
                    if (!cancel.IsCancellationRequested)
                        logger.LogCritical(ex, $"Unhandled exception on constructables");
                }
            });
        }

        protected virtual void SetCacheHeadersIfNecessary(HttpContext ctx)
        {
            if (NoCacheExtensions.Contains(Path.GetExtension(ctx.Request.Path)))
            {
                ctx.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate";
                ctx.Response.Headers["Pragma"] = "no-cache";
                ctx.Response.Headers["Expires"] = "0";
            }
        }

        protected virtual void LogStartup(ILogger logger, IToastProvider? toastProvider)
        {
            if (AppsettingsSafeModeException != null)
            {
                logger.LogError(AppsettingsSafeModeException, """
                    Application has been started in appsettings safe-mode, i.e. without user overridable options,
                    due to exception while initializing. 
                    Please try to fix configuration issues and restart the application.
                    """);
                toastProvider?.Show(new ToastMessage
                {
                    HeaderText = "Started in appsettings safe-mode",
                    BodyText = """
                    Application has been started in appsettings safe-mode, i.e. without user overridable options,
                    due to exception while initializing. 
                    Please see the log files and try to fix configuration issues.
                    """,
                    Type = ToastMessageType.Error,
                });
            }
            logger.LogInformation($"Configuration was loaded from: {Environment.NewLine}{string.Join(Environment.NewLine, ConfigurationSources)}");
        }

        protected virtual void StartBrowser(IServer server, ApplicationOptions options)
        {
            if (!options.StartBrowser)
                return;
            if (string.IsNullOrWhiteSpace(options.BrowserCommand))
            {
                var feature = server.Features.Get<IServerAddressesFeature>();
                Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = feature!.Addresses
                        .Order()
                        .First()
                        .Replace("[::]", "localhost")
                        .Replace("0.0.0.0", "localhost"),
                });
            }
            else
            {
                Process.Start(new ProcessStartInfo
                {
                    UseShellExecute = true,
                    FileName = options.BrowserCommand,
                    Arguments = options.BrowserArgs,
                });
            }
        }
    }
}
