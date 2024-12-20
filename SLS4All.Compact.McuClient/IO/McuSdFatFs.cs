// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Collections;
using SLS4All.Compact.IO.FatFs;
using SLS4All.Compact.McuClient.Pins;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public sealed class McuSdFatFs : FatFsBase
    {
        private readonly IMcuSdCard _sd;
        private readonly CancellationToken _cancel;
        private readonly MemoryStream _ms;
        private readonly FATFS _fs;

        public string Drive => "0:";

        public McuSdFatFs(IMcuSdCard sd, CancellationToken cancel)
        {
            _sd = sd;
            _cancel = cancel;
            _ms = new();

            _fs = new();
            CheckResult(f_mount(_fs, Drive, 0));
        }

        protected override DRESULT disk_initialize(byte pdrv)
        {
            return DRESULT.RES_OK;
        }

        protected override DRESULT disk_ioctl(byte pdrv, byte cmd, Span<byte> buff)
        {
            DRESULT result;
            switch (cmd)
            {
                case CTRL_SYNC:
                    result = DRESULT.RES_OK;
                    break;

                case GET_BLOCK_SIZE:
                    result = DRESULT.RES_PARERR;
                    break;

                case GET_SECTOR_SIZE:
                    MemoryMarshal.Cast<byte, ushort>(buff)[0] = checked((ushort)_sd.SectorSize);
                    result = DRESULT.RES_OK;
                    break;

                case GET_SECTOR_COUNT:
                    MemoryMarshal.Cast<byte, uint>(buff)[0] = (uint)Math.Min(_sd.TotalSectors, uint.MaxValue);
                    result = DRESULT.RES_OK;
                    break;

                default:
                    result = DRESULT.RES_ERROR;
                    break;
            }
            return result;
        }

        protected override DRESULT disk_read(byte pdrv, Span<byte> buff, ulong sector, uint count)
        {
            _ms.SetLength(0);
            _sd.ReadSectors(checked((uint)sector), checked((int)count), _ms, _cancel).GetAwaiter().GetResult();
            _ms.AsSpan().CopyTo(buff);
            return DRESULT.RES_OK;
        }

        protected override DRESULT disk_status(byte pdrv)
        {
            return DRESULT.RES_OK;
        }

        protected override DRESULT disk_write(byte pdrv, ReadOnlySpan<byte> buff, ulong sector, uint count)
        {
            _ms.SetLength(0);
            _ms.Write(buff);
            _ms.Position = 0;
            _sd.WriteSectors(checked((uint)sector), checked((int)count), _ms, _cancel).GetAwaiter().GetResult();
            return DRESULT.RES_OK;
        }

        public void Mount()
        {
            CheckResult(f_mount(_fs, Drive, 1));
        }

        public void Unmount()
        {
            CheckResult(f_mount(null, Drive, 1), allowedStates: [FRESULT.FR_NOT_ENABLED]);
        }

        public void MakeFS()
        {
            CheckResult(f_mkfs(Drive, null));
        }
    }
}
