// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public sealed class WorkingDirectoryAppDataWriter : IAppDataWriter
    {
        public static WorkingDirectoryAppDataWriter Instance { get; } = new();

        public string GetBackupsDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.BackupsDirectory);

        public string GetBaseDirectory()
            => Path.Combine(Environment.CurrentDirectory);

        public string GetJobsDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.JobsDirectory);

        public string GetNonMemoryTempDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.NonMemoryTempDirectory);

        public string GetObjectsDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.ObjectsDirectory);

        public string GetPersistentTempDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.PersistentTempDirectory);

        public string GetPrintProfilesDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.PrintProfilesDirectory);

        public string GetPrintSessionsDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.PrintSessionsDirectory);

        public string GetPrivateDataDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.PrivateDataDirectory);

        public string GetPrivateOptionsFilename(Type optionsType)
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.PrivateOptionsDirectory, optionsType.FullName!);

        public string GetPublicOptionsDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.PublicOptionsTypeDirectory);

        public string GetSurfaceDirectory()
            => Path.Combine(Environment.CurrentDirectory, IAppDataWriter.SurfaceDirectory);
    }
}
