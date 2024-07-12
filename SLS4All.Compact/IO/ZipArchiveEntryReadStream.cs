// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public sealed class ZipArchiveEntryReadStream : WrapperStreamBase
    {
        private readonly Stream _archiveStream;
        private readonly ZipArchive _archive;
        private readonly ZipArchiveEntry _entry;

        public ZipArchiveEntryReadStream(Stream archiveStream, string entryName)
            : base(null!)
        {
            _archiveStream = archiveStream;
            _archive = new ZipArchive(archiveStream, ZipArchiveMode.Read);
            _entry = _archive.GetEntry(entryName) ?? throw new FileNotFoundException($"Entry {entryName} was not found in Job");
            InnerStream = _entry.Open();
        }

        public override void Close()
        {
            base.Close();
            _archive?.Dispose();
            _archiveStream?.Dispose();
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            _archive?.Dispose();
            _archiveStream?.Dispose();
        }
    }
}
