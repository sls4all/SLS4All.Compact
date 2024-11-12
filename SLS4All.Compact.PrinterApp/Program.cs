// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SLS4All.Compact.DependencyInjection;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Nesting;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Processing.Meshes;
using SLS4All.Compact.Slicing;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tomlet.Exceptions;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.Diagnostics;
using System.Runtime.CompilerServices;

namespace SLS4All.Compact
{
    public class Program
    {
        private const int _minWorkerThreads = 24;
        private const int _minCompletionPortThreads = 8;

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

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static async Task MainInner(string[] args)
        {
            if (Array.IndexOf(args, "--debug") != -1)
                DebuggerHelpers.WaitForDebugger();

            SetMinThreads();

            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(StartupBase).Assembly);
            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(IPrinterClient).Assembly);
            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(IMcu).Assembly);
            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(ExhaustiveNester).Assembly);
            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(Mesh).Assembly);
            CompactServiceCollectionExtensions.ScanAssemblies.Add(typeof(BitmapSliceProcessor).Assembly);

            using (var host = CreateHostBuilder<Startup>(args).Build())
            {
                var logger = host.Services.GetRequiredService<ILogger<Program>>();
                try
                {
                    logger.LogInformation($"Host is starting to run at {DateTime.UtcNow} UTC");
                    await host.RunAsync();
                    logger.LogInformation($"Host has finished running at {DateTime.UtcNow} UTC");
                }
                catch (Exception ex)
                {
                    logger.LogCritical(ex, $"Unhandled exception in host run, application is crashing at {DateTime.UtcNow} UTC");
                    throw;
                }
            }
        }

        protected static void SetMinThreads()
        {
            // NOTE: PrinterApp is very demanding due to combination of web/printing/low-level processing.
            //       Increase default minimum number of thread-pool threads
            ThreadPool.GetMinThreads(out var workerThread, out var completionPortThreads);
            var newWorkerThread = Math.Max(workerThread * 2, _minWorkerThreads);
            var newCompletionPortThreads = Math.Max(completionPortThreads * 2, _minCompletionPortThreads);
            if (newWorkerThread > workerThread || newCompletionPortThreads > completionPortThreads)
            {
                Console.WriteLine($"Increasing min TP: Worker {workerThread}->{newWorkerThread}, IO {completionPortThreads}->{newCompletionPortThreads}");
                ThreadPool.SetMinThreads(newWorkerThread, newCompletionPortThreads);
            }
        }

        public static IHostBuilder CreateHostBuilder<TStartup>(string[] args)
            where TStartup : class
            => Host.CreateDefaultBuilder(args)
                .ConfigureAppConfiguration((context, builder) =>
                {
                    var intermediate = SetupConfigurationBuilder(args, context, builder);
                    var optionsWriterFiles = UserProfileAppDataWriter.GetPrivateOptionsFilenames(
                        intermediate,
                        StartupBase.AppDataWriterSection,
                        StartupBase.OptionsWriterTypes.ToArray());
                    foreach (var filename in optionsWriterFiles)
                        builder.AddJsonFile(filename, optional: true, reloadOnChange: true);
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<TStartup>();
                });

        private static bool ExistsConfigurationFile(string path)
        {
            var json = path + ".json";
            var toml = path + ".toml";
            return File.Exists(json) || File.Exists(toml);
        }

        private static void AddConfigurationFile(IConfigurationBuilder builder, string path, bool reloadOnChange, List<string> files)
        {
            if (Path.GetExtension(path).Equals(".toml", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddTomlFile(path, true, reloadOnChange);
                if (File.Exists(path))
                    files.Add(Path.GetFullPath(path));
            }
            else if (Path.GetExtension(path).Equals(".json", StringComparison.OrdinalIgnoreCase))
            {
                builder.AddJsonFile(path, true, reloadOnChange);
                if (File.Exists(path))
                    files.Add(Path.GetFullPath(path));
            }
            else
            {
                var json = path + ".json";
                var toml = path + ".toml";
                builder.AddJsonFile(json, true, reloadOnChange);
                if (File.Exists(json))
                    files.Add(Path.GetFullPath(json));
                builder.AddTomlFile(toml, true, reloadOnChange);
                if (File.Exists(toml))
                    files.Add(Path.GetFullPath(toml));
            }
        }

        private static IConfigurationRoot SetupConfigurationBuilder(string[] args, HostBuilderContext context, IConfigurationBuilder builder)
        {
            var env = context.HostingEnvironment;
            const string userFile = "appsettings.user";
            const int defaultOrder = 0;
            const int userOrder = 1;
            var loadedFiles = new List<(int order, string filename)>
            {
                (defaultOrder, "appsettings"),
                (defaultOrder, $"appsettings.{env.EnvironmentName}"),
                (userOrder, userFile),
                (userOrder, $"appsettings.{env.EnvironmentName}.user"),
            };
            var initalConfig = builder.Build();
            var publicOptionsDirectory = UserProfileAppDataWriter.GetPublicOptionsDirectory(initalConfig, StartupBase.ApplicationSection);
            Directory.CreateDirectory(publicOptionsDirectory);
            var userFilePath = Path.Combine(publicOptionsDirectory, userFile);
            var userFilePathToml = userFilePath + ".toml";
            if (!File.Exists(userFilePathToml))
                File.WriteAllText(userFilePathToml,
                    """
                    # Place custom user options here, e.g.:
                    [OptionsClassName]
                    Property1 = "text value"
                    Property2 = 3.14
                    Property3 = true
                    Property4 = [ 1, 2, 3 ]
                    Property5.SubProperty = "something"
                    """);

            string[] Apply(IConfigurationBuilder builder)
            {
                while (builder.Sources.Count > 1)
                    builder.Sources.RemoveAt(builder.Sources.Count - 1);

                if (env.IsDevelopment() && env.ApplicationName is { Length: > 0 })
                {
                    var appAssembly = Assembly.Load(new AssemblyName(env.ApplicationName));
                    if (appAssembly is not null)
                    {
                        builder.AddUserSecrets(appAssembly, optional: true, reloadOnChange: true);
                    }
                }
                builder.Add<JsonStreamConfigurationSource>(source => source.Stream = typeof(StartupBase).Assembly.GetManifestResourceStream("SLS4All.Compact.appsettings.storage-default.json")!);
                var files = new List<string>();
                foreach (var item in loadedFiles.OrderBy(x => x.order))
                {
                    if (Startup.AppsettingsSafeModeEnabled && (item.filename == userFilePath || item.filename == userFile))
                        continue;
                    var publicFilename = Path.Combine(publicOptionsDirectory, item.filename);
                    string filename = !Startup.AppsettingsSafeModeEnabled && ExistsConfigurationFile(publicFilename)
                        ? publicFilename
                        : item.filename;
                    AddConfigurationFile(builder, filename, true, files);
                }

                builder.AddUserSecrets<Program>();
                builder.AddEnvironmentVariables();
                if (args is { Length: > 0 })
                {
                    builder.AddCommandLine(args);
                }
                return files.ToArray();
            }
            while (true)
            {
                Apply(builder);

                IConfigurationRoot config;
                try
                {
                    config = builder.Build();
                }
                catch (Exception ex) when (!Startup.AppsettingsSafeModeEnabled && 
                    (ex is JsonException || ex is TomlException || ex is FormatException || ex is InvalidDataException))
                {
                    Startup.AppsettingsSafeModeException = ex;
                    Startup.AppsettingsSafeModeEnabled = true;
                    Console.WriteLine($"Exception while loading appsettings files: {ex.Message}");
                    Console.WriteLine($"Will try to build configuration without user overridable files!");
                    continue;
                }
                var options = new ApplicationOptions();
                config.Bind(StartupBase.ApplicationSection, options);
                var modified = false;
                foreach (var include in options.Includes)
                {
                    if (!string.IsNullOrWhiteSpace(include) &&
                        !loadedFiles.Any(x => x.filename == include))
                    {
                        loadedFiles.Add((defaultOrder, include));
                        modified = true;
                    }
                }
                foreach (var dependency in options.Dependencies.Values.Where(x => x.IsEnabled))
                {
                    if (!loadedFiles.Any(x => x.filename == dependency.Filename))
                    {
                        var beforeIndex = loadedFiles.FindIndex(x => x.filename == dependency.Before);
                        var afterIndex = loadedFiles.FindIndex(x => x.filename == dependency.After);
                        if (beforeIndex != -1)
                            loadedFiles.Insert(beforeIndex, (loadedFiles[beforeIndex].order, dependency.Filename));
                        else if (afterIndex != -1)
                            loadedFiles.Insert(afterIndex + 1, (loadedFiles[afterIndex].order, dependency.Filename));
                        else
                            loadedFiles.Add((defaultOrder, dependency.Filename));
                        modified = true;
                    }
                }
                if (!modified)
                {
                    Startup.ConfigurationSources = Apply(builder); // reset streams
                    return config;
                }
            }
        }
    }
}
