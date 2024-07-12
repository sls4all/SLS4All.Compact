// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿namespace SLS4All.Compact.Configuration
{
    public interface IAppDataWriter
    {
        public const string PrivateOptionsDirectory = ".PrivateConfiguration";
        public const string PrivateDataDirectory = ".PrivateData";
        public const string NonMemoryTempDirectory = ".Temp";
        public const string PersistentTempDirectory = ".TempPersistent";
        public const string PublicOptionsTypeDirectory = "Configuration";
        public const string PrintSessionsDirectory = "PrintSessions";
        public const string PrintProfilesDirectory = "PrintProfiles";
        public const string JobsDirectory = "Jobs";
        public const string BackupsDirectory = "Backups";
        public const string ObjectsDirectory = "Objects";
        public const string ArchiveDirectory = "Archive";
        public const string PreviousDirectory = "Previous";
        public const string CurrentDirectory = "Current";
        public const string StagingDirectory = "Staging";
        public const string PrepareDirectory = "Prepare";
        public const string SurfaceDirectory = "Surface";

        string GetBaseDirectory();
        string GetBackupsDirectory();
        string GetSurfaceDirectory();
        string GetNonMemoryTempDirectory();
        string GetPersistentTempDirectory();
        string GetPrivateDataDirectory();
        string GetPrintSessionsDirectory();
        string GetPrintProfilesDirectory();
        string GetJobsDirectory();
        string GetObjectsDirectory();
        string GetPublicOptionsDirectory();
        string GetPrivateOptionsFilename(Type optionsType);
    }
}