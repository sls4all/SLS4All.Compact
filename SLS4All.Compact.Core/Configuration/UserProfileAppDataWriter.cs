// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using System;
using System.IO;
using System.Linq;

namespace SLS4All.Compact.Configuration
{
    public class UserProfileAppDataWriterOptions
    {
        public string BasePath { get; set; } = "SLS4All";
    }

    public sealed class UserProfileAppDataWriter : IAppDataWriter
    {
        private readonly IOptions<UserProfileAppDataWriterOptions> _options;

        public UserProfileAppDataWriter(IOptions<UserProfileAppDataWriterOptions> options)
        {
            _options = options;
            var baseDirectory = GetBaseDirectory();
            Directory.CreateDirectory(baseDirectory);
            var nonMemoryTempDirectory = GetNonMemoryTempDirectory();
            if (Directory.Exists(nonMemoryTempDirectory))
                Directory.Delete(nonMemoryTempDirectory, true);
            Directory.CreateDirectory(nonMemoryTempDirectory);
            var persistentTempDirectory = GetPersistentTempDirectory();
            Directory.CreateDirectory(persistentTempDirectory);
        }

        public string GetBaseDirectory()
            => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), _options.Value.BasePath);

        public static string GetDirectory(UserProfileAppDataWriterOptions options, string typeDirectory)
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                options.BasePath,
                typeDirectory);
        }

        public string GetNonMemoryTempDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.NonMemoryTempDirectory);

        public string GetPersistentTempDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.PersistentTempDirectory);

        public string GetPrivateDataDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.PrivateDataDirectory);

        public string GetPublicOptionsDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.PublicOptionsTypeDirectory);

        public static string GetPublicOptionsDirectory(IConfigurationRoot configuration, string section)
        {
            var options = new UserProfileAppDataWriterOptions();
            configuration.Bind(section, options);
            return GetDirectory(options, IAppDataWriter.PublicOptionsTypeDirectory);
        }

        public string GetPrintSessionsDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.PrintSessionsDirectory);

        public string GetPrintProfilesDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.PrintProfilesDirectory);

        public string GetJobsDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.JobsDirectory);

        public string GetObjectsDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.ObjectsDirectory);

        public string GetBackupsDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.BackupsDirectory);

        public string GetSurfaceDirectory()
            => GetDirectory(_options.Value, IAppDataWriter.SurfaceDirectory);

        public string GetPrivateOptionsFilename(Type optionsType)
            => GetPrivateOptionsFilename(_options.Value.BasePath, optionsType);

        public static string GetPrivateOptionsFilename(string basePath, Type optionsType)
        {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                basePath,
                IAppDataWriter.PrivateOptionsDirectory,
                optionsType.FullName + ".json");
            return path;
        }

        public static string[] GetPrivateOptionsFilenames(IConfigurationRoot configuration, string section, Type[] optionsTypes)
        {
            var options = new UserProfileAppDataWriterOptions();
            configuration.Bind(section, options);
            return optionsTypes.Select(x => GetPrivateOptionsFilename(options.BasePath, x)).ToArray();
        }

        public static string GetOptionsSectionName(Type optionsType)
            => optionsType.FullName!;
    }
}
