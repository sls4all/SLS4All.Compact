// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Threading.Tasks;
using BYTE = byte; /* char must be 8-bit */
using WORD = ushort; /* 16-bit unsigned */
using DWORD = uint; /* 32-bit unsigned */
using TCHAR = char;
using WCHAR = char;
using static SLS4All.Compact.IO.FatFs.FatFsBase.FRESULT;
using static SLS4All.Compact.IO.FatFs.FatFsBase.DRESULT;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Buffers.Binary;

namespace SLS4All.Compact.IO.FatFs
{
    public unsafe sealed class RamFatFs : FatFsBase
    {
        public const int SECTORSIZE = 512;
        private FATFS[] _fs = [];
        private readonly Memory<byte>[] _diskMem = new Memory<byte>[FF_VOLUMES];
        private readonly uint[] _numSectors = new uint[FF_VOLUMES];

        protected override DRESULT disk_initialize(byte pdrv)
        {
            return RES_OK;
        }

        protected override DRESULT disk_status(byte pdrv)
        {
            return RES_OK;
        }

        protected override unsafe DRESULT disk_read(byte pdrv, Span<byte> buff, ulong sector, uint count)
        {
            _diskMem[pdrv].Span.Slice(checked((int)(sector * SECTORSIZE)), checked((int)count * SECTORSIZE)).CopyTo(buff);
            return RES_OK;
        }

        protected override unsafe DRESULT disk_write(byte pdrv, ReadOnlySpan<byte> buff, ulong sector, uint count)
        {
            buff.Slice(0, checked((int)count * SECTORSIZE)).CopyTo(_diskMem[pdrv].Span.Slice(checked((int)(sector * SECTORSIZE))));
            return RES_OK;
        }

        protected override unsafe DRESULT disk_ioctl(byte pdrv, byte cmd, Span<byte> buff)
        {
            DRESULT result;

            switch (cmd)
            {
                case CTRL_SYNC:
                    result = RES_OK;
                    break;

                case GET_BLOCK_SIZE:
                    result = RES_PARERR;
                    break;

                case GET_SECTOR_SIZE:
                    MemoryMarshal.Cast<byte, WORD>(buff)[0] = SECTORSIZE;
                    result = RES_OK;
                    break;

                case GET_SECTOR_COUNT:
                    MemoryMarshal.Cast<byte, DWORD>(buff)[0] = _numSectors[pdrv];
                    result = RES_OK;
                    break;

                default:
                    result = RES_ERROR;
                    break;
            }

            return (result);
        }

        public FRESULT Mount(ReadOnlySpan<TCHAR> pathSpan, Memory<byte> buffer, bool mkfs, MKFS_PARM? mkfsParam = null)
        {
            ReadOnlyPtr<TCHAR> path = pathSpan;
            var pathCopy = path;
            var drive = get_ldnumber(ref pathCopy);
            if (drive < 0)
                return FR_OK;
            if (drive >= _fs.Length)
                Array.Resize(ref _fs, drive + 1);
            if (_fs[drive] != null)
                return FRESULT.FR_DISK_ERR;

            var fs = new FATFS();
            _diskMem[drive] = buffer;
            _numSectors[drive] = (DWORD)(buffer.Length / SECTORSIZE);

            if (mkfs)
            {
                buffer.Span.Clear();
                var res = f_mkfs(path, mkfsParam);
                if (res != FR_OK)
                    return res;
            }

            /* mount the drive */
            var mountRes = f_mount(fs, path, 1);
            if (mountRes != FR_OK)
                return mountRes;
            _fs[drive] = fs;

            return FR_OK;
        }

        public FRESULT Unmount(ReadOnlySpan<TCHAR> pathSpan)
        {
            ReadOnlyPtr<TCHAR> path = pathSpan;
            var pathCopy = path;
            var drive = get_ldnumber(ref pathCopy);
            if (drive < 0)
                return FRESULT.FR_DISK_ERR;
            if (drive >= _fs.Length)
                Array.Resize(ref _fs, drive + 1);
            if (_fs[drive] == null)
                return FRESULT.FR_DISK_ERR;

            var mountRes = f_mount(null, path, 1);
            if (mountRes != FR_NOT_ENABLED)
                return mountRes;
            _fs[drive] = null!;
            return FR_OK;
        }
    }
}
