// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using SLS4All.Compact.DependencyInjection;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.Printer;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.McuClient.PipedMcu;
using Microsoft.Extensions.Options;
using SLS4All.Compact.McuClient.Devices;
using NReco.Logging.File;
using SLS4All.Compact.Threading;

namespace SLS4All.Compact.McuApp
{
    internal class Program
    {
        public static Task Main(string[] args)
        {
            AppDomain.CurrentDomain.AssemblyResolve += OnAssemblyResolve;
            return MainInner(args);
        }

        private static Assembly? OnAssemblyResolve(object? sender, ResolveEventArgs args)
        {
            // NOTE: allow loading of SLS4All assemblies of different version than requested
            //       this may happen when NuGet packages request a specific version but local project the assembly was buit is of different or no version
            var name = new AssemblyName(args.Name);
            if (name.Name?.StartsWith("SLS4All.") == true && name.Version != null)
            {
                name.Version = null;
                return Assembly.Load(name);
            }
            else
                return null;
        }

        static async Task MainInner(string[] args)
        {
            if (Array.IndexOf(args, "--debug") != -1)
                DebuggerHelpers.WaitForDebugger();

            // disable paging for this process so system I/O cant affect us as much
            bool? disabledPaging = null;
            if (Array.IndexOf(args, "--no-paging") != -1)
                disabledPaging = PrinterGC.TryDisableProcessPaging();

            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(Program).Assembly);
            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(IPrinterClient).Assembly);
            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(IMcu).Assembly);

            var builder = Host.CreateApplicationBuilder(args);

            // setup configuration
            builder.Services.Configure<UserProfileAppDataWriterOptions>(builder.Configuration.GetSection("UserProfileAppDataWriter"));
            builder.Services.AddAsImplementationAndInterfaces<UserProfileAppDataWriter>(ServiceLifetime.Singleton);
            SetupConfiguration(args, builder, [], null);
            var publicOptionsDirectory = UserProfileAppDataWriter.GetPublicOptionsDirectory(builder.Configuration, "Application");
            Directory.CreateDirectory(publicOptionsDirectory);
            var applicationOptions = new McuAppApplicationOptions();
            builder.Configuration.GetSection("Application").Bind(applicationOptions);
            var configurationSources = SetupConfiguration(args, builder, applicationOptions.ConfigurationSources, publicOptionsDirectory);

            // setup logging
            builder.Services.AddLogging(loggingBuilder =>
            {
                var loggingSection = builder.Configuration.GetSection("Logging");
                var path = loggingSection["File:Path"];
                if (path != null)
                    loggingSection["File:Path"] = Path.ChangeExtension(path, ".mcuapp" + Path.GetExtension(path));
                loggingBuilder.AddFile(loggingSection);
                loggingBuilder.AddConsole(options => options.FormatterName = CompactSimpleConsoleFormatter.FormatterName)
                    .AddConsoleFormatter<CompactSimpleConsoleFormatter, SimpleConsoleFormatterOptions>();
            });

            // setup services
            builder.Services.AddOptions();
            builder.Services.Configure<McuManagerOptions>(builder.Configuration.GetSection("McuManager"));
            builder.Services.AddAsImplementationAndParents<PipedMcuManagerLocal>(ServiceLifetime.Singleton);
            builder.Services.AddAsImplementationAndInterfaces<PipedMcuComponent>(ServiceLifetime.Singleton);
            builder.Services.AddAsImplementationAndInterfaces<NullPrinterSettings>(ServiceLifetime.Singleton);
            builder.Services.AddAsImplementationAndInterfaces<NullThreadStackTraceDumper>(ServiceLifetime.Singleton);

            builder.Services.Configure<McuAliasesOptions>(builder.Configuration.GetSection("McuAliases"));
            switch (applicationOptions.McuSerialDeviceFactory)
            {
                case McuAppSerialDeviceFactoryType.LocalSerial:
                    builder.Services.Configure<McuLocalSerialDeviceFactoryOptions>(builder.Configuration.GetSection("McuLocalSerialDeviceFactory"));
                    builder.Services.AddAsImplementationAndInterfaces<McuLocalSerialDeviceFactory>(ServiceLifetime.Singleton);
                    break;
                case McuAppSerialDeviceFactoryType.ProxySerial:
                    builder.Services.Configure<McuProxySerialDeviceFactoryOptions>(builder.Configuration.GetSection("McuProxySerialDeviceFactory"));
                    builder.Services.AddAsImplementationAndInterfaces<McuProxySerialDeviceFactory>(ServiceLifetime.Singleton);
                    break;
                case McuAppSerialDeviceFactoryType.SshSerial:
                    builder.Services.Configure<McuSshSerialDeviceFactoryOptions>(builder.Configuration.GetSection("McuSshSerialDeviceFactory"));
                    builder.Services.AddAsImplementationAndInterfaces<McuSshSerialDeviceFactory>(ServiceLifetime.Singleton);
                    break;
            }

            using (var host = builder.Build())
            {
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    logger.LogInformation($"MCU Host is starting to run at {DateTime.UtcNow} UTC");
                    if (disabledPaging == false)
                        logger.LogWarning($"Failed to disable system paging for this process");
                    logger.LogInformation($"Configuration was loaded from: {Environment.NewLine}{configurationSources}");
                    await host.RunAsync();
                    logger.LogInformation($"MCU Host has finished running at {DateTime.UtcNow} UTC");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, $"Unhandled exception in MCU Host run, application is crashing at {DateTime.UtcNow} UTC");
                    throw;
                }
            }
        }

        private static string SetupConfiguration(
            string[] args, 
            HostApplicationBuilder builder, 
            string[] configurationSources,
            string? publicOptionsDirectory)
        {
            const string userFile = "appsettings.user";
            string userFilename = userFile;
            if (publicOptionsDirectory != null)
            {
                var publicUserFile = Path.Combine(publicOptionsDirectory, userFile);
                if (ExistsConfigurationFile(publicUserFile))
                    userFilename = userFile;
            }
            var buf = new StringBuilder();
            while (builder.Configuration.Sources.Count > 1)
                builder.Configuration.Sources.RemoveAt(builder.Configuration.Sources.Count - 1);
            if (builder.Environment.IsDevelopment() && builder.Environment.ApplicationName is { Length: > 0 })
            {
                var appAssembly = Assembly.Load(new AssemblyName(builder.Environment.ApplicationName));
                if (appAssembly is not null)
                {
                    builder.Configuration.AddUserSecrets(appAssembly, optional: true, reloadOnChange: true);
                }
            }
            AddConfigurationFile(builder.Configuration, "appsettings_mcuapp", true, buf);
            AddConfigurationFile(builder.Configuration, $"appsettings_mcuapp.{builder.Environment.EnvironmentName}", true, buf);
            AddConfigurationFile(builder.Configuration, userFilename, true, buf);
            AddConfigurationFile(builder.Configuration, $"appsettings_mcuapp.{builder.Environment.EnvironmentName}.user", true, buf);
            foreach (var source in configurationSources)
                AddConfigurationFile(builder.Configuration, source, true, buf);
            builder.Configuration.AddUserSecrets<Program>();
            builder.Configuration.AddEnvironmentVariables();
            if (args is { Length: > 0 })
            {
                builder.Configuration.AddCommandLine(args);
            }
            return buf.ToString().Trim();
        }

        private static void AddConfigurationFile(IConfigurationBuilder builder, string path, bool reloadOnChange, StringBuilder buf)
        {
            if (Path.GetExtension(path).Equals(".toml", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddTomlFile(path, true, reloadOnChange);
                if (File.Exists(path))
                    buf.AppendLine($"{path}");
            }
            else if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddJsonFile(path, true, reloadOnChange);
                if (File.Exists(path))
                    buf.AppendLine($"{path}");
            }
            else
            {
                var json = path + ".json";
                var toml = path + ".toml";
                builder.AddJsonFile(json, true, reloadOnChange);
                if (File.Exists(json))
                    buf.AppendLine($"{path} ({json})");
                builder.AddTomlFile(toml, true, reloadOnChange);
                if (File.Exists(toml))
                    buf.AppendLine($"{path} ({toml})");
            }
        }

        private static bool ExistsConfigurationFile(string path)
        {
            var json = path + ".json";
            var toml = path + ".toml";
            return File.Exists(json) || File.Exists(toml);
        }
    }
}
