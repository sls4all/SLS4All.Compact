// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Dynamic;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public static class FatFirmwareImageBuilder
    {
        public static Stream CreateImage(Stream firmware)
        {
            const int firmwareSizeOffset = 0x101C;
            const int firmwareBaseOffset = 0x5000;
            const long maxFirmwareSize = 0xffff;
            var working = new MemoryStream();
            using (var templateGz = typeof(FatFirmwareImageBuilder).Assembly.GetManifestResourceStream("SLS4All.Compact.IO.FatFirmwareImageTemplate.img.gz")!)
            using (var template = new GZipStream(templateGz, CompressionMode.Decompress))
            {
                template.CopyTo(working);
                working.Position = firmwareBaseOffset;
                firmware.CopyTo(working);
                var size = working.Position - firmwareBaseOffset;
                if (size > maxFirmwareSize)
                    throw new InvalidOperationException("Firmware is too large");
                var data = working.AsSpan();
                BinaryPrimitives.WriteUInt32LittleEndian(data.Slice(firmwareSizeOffset), (uint)size);
                working.Position = 0;
                return working;
            }
        }
    }
}
