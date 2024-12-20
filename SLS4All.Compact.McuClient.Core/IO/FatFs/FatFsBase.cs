/*----------------------------------------------------------------------------/
/  FatFs - Generic FAT Filesystem module  R0.15a                              /
/-----------------------------------------------------------------------------/
/
/ Copyright (C) 2024, ChaN, all right reserved.
/
/ FatFs module is an open source software. Redistribution and use of FatFs in
/ source and binary forms, with or without modification, are permitted provided
/ that the following condition is met:

/ 1. Redistributions of source code must retain the above copyright notice,
/    this condition and the following disclaimer.
/
/ This software is provided by the copyright holder and contributors "AS IS"
/ and any warranties related to this software are DISCLAIMED.
/ The copyright owner or contributors be NOT LIABLE for any damages caused
/ by use of this software.
/
/----------------------------------------------------------------------------*/

#pragma warning disable CS9084
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UINT = uint; /* int must be 16-bit or 32-bit */
using BYTE = byte; /* char must be 8-bit */
using WORD = ushort; /* 16-bit unsigned */
using DWORD = uint; /* 32-bit unsigned */
using QWORD = ulong; /* 64-bit unsigned */
using WCHAR = char; /* UTF-16 code unit */
using TCHAR = char; /* Type of path name strings on FatFs API (TCHAR) */
/* Type of file size and LBA variables */
using FSIZE_t = ulong;
using LBA_t = ulong;
using static SLS4All.Compact.IO.FatFs.FatFsBase.FRESULT;
using static SLS4All.Compact.IO.FatFs.FatFsBase.DRESULT;
using DSTATUS = byte;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Diagnostics.CodeAnalysis;
using System.Buffers.Binary;
using System.Buffers;
using System.Reflection.Metadata.Ecma335;
using static SLS4All.Compact.IO.FatFs.FatFsBase;
using System.Globalization;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace SLS4All.Compact.IO.FatFs
{
    public abstract class FatFsBase
    {
        public ref struct Ptr<T>(Span<T> span)
        {
            public Span<T> Span = span;

            public ref T Value => ref Span[0];

            public bool IsEmpty => Span.IsEmpty;

            public ref T this[int index]
                => ref Span[index];

            public ref T this[uint index]
                => ref Span[checked((int)index)];

            public static Ptr<T> operator ++(Ptr<T> ptr)
                => new Ptr<T>(ptr.Span.Slice(1));

            public static Ptr<T> operator +(Ptr<T> ptr, int count)
                => new Ptr<T>(ptr.Span.Slice(count));

            public static Ptr<T> operator +(Ptr<T> ptr, uint count)
                => new Ptr<T>(ptr.Span.Slice(checked((int)count)));

            public static bool operator ==(Ptr<T> x, Ptr<T> y)
                => Unsafe.AreSame(ref MemoryMarshal.GetReference(x.Span), ref MemoryMarshal.GetReference(y.Span));

            public static bool operator !=(Ptr<T> x, Ptr<T> y)
                => !(x == y);

            public static implicit operator Ptr<T>(Span<T> span)
                => new Ptr<T>(span);

            public static implicit operator Ptr<T>(T[] array)
                => new Ptr<T>(array);

            public static implicit operator Span<T>(Ptr<T> ptr)
                => ptr.Span;

            public static implicit operator ReadOnlySpan<T>(Ptr<T> ptr)
                => ptr.Span;

            public override bool Equals([NotNullWhen(true)] object? obj)
                => throw new NotSupportedException();

            public override int GetHashCode()
                => throw new NotSupportedException();
        }

        public ref struct ReadOnlyPtr<T>(ReadOnlySpan<T> span)
        {
            public ReadOnlySpan<T> Span = span;

            public ref readonly T Value => ref Span[0];

            public bool IsEmpty => Span.IsEmpty;

            public ref readonly T this[int index]
                => ref Span[index];

            public ref readonly T this[uint index]
                => ref Span[checked((int)index)];

            public static ReadOnlyPtr<T> operator ++(ReadOnlyPtr<T> ptr)
                => new ReadOnlyPtr<T>(ptr.Span.Slice(1));

            public static ReadOnlyPtr<T> operator +(ReadOnlyPtr<T> ptr, int count)
                => new ReadOnlyPtr<T>(ptr.Span.Slice(count));

            public static ReadOnlyPtr<T> operator +(ReadOnlyPtr<T> ptr, uint count)
                => new ReadOnlyPtr<T>(ptr.Span.Slice(checked((int)count)));

            public static bool operator ==(ReadOnlyPtr<T> x, ReadOnlyPtr<T> y)
                => Unsafe.AreSame(ref MemoryMarshal.GetReference(x.Span), ref MemoryMarshal.GetReference(y.Span));

            public static bool operator !=(ReadOnlyPtr<T> x, ReadOnlyPtr<T> y)
                => !(x == y);

            public static implicit operator ReadOnlyPtr<T>(Ptr<T> ptr)
                 => new ReadOnlyPtr<T>(ptr.Span);

            public static implicit operator ReadOnlyPtr<T>(Span<T> span)
                => new ReadOnlyPtr<T>(span);

            public static implicit operator ReadOnlyPtr<T>(ReadOnlySpan<T> span)
                => new ReadOnlyPtr<T>(span);

            public static implicit operator ReadOnlyPtr<T>(T[] array)
                => new ReadOnlyPtr<T>(array);

            public static implicit operator ReadOnlySpan<T>(ReadOnlyPtr<T> ptr)
                => ptr.Span;

            public override bool Equals([NotNullWhen(true)] object? obj)
                => throw new NotSupportedException();

            public override int GetHashCode()
                => throw new NotSupportedException();
        }

        public const int FF_VOLUMES = 10;
        public const BYTE CTRL_SYNC = 0;    /* Complete pending write process (needed at FF_FS_READONLY == 0) */
        public const BYTE GET_SECTOR_COUNT = 1; /* Get media size (needed at FF_USE_MKFS == 1) */
        public const BYTE GET_SECTOR_SIZE = 2;  /* Get sector size (needed at FF_MAX_SS != FF_MIN_SS) */
        public const BYTE GET_BLOCK_SIZE = 3;   /* Get erase block size (needed at FF_USE_MKFS == 1) */
        public const BYTE CTRL_TRIM = 4;	/* Inform device that the data on the block of sectors is no longer used (needed at FF_USE_TRIM == 1) */

        protected virtual PARTITION VolToPart(int vol)
            => new PARTITION { pd = (BYTE)vol, pt = 0 };
        protected abstract DRESULT disk_initialize(BYTE pdrv);
        protected abstract DRESULT disk_status(BYTE pdrv);
        protected abstract DRESULT disk_read(BYTE pdrv, Span<BYTE> buff, LBA_t sector, UINT count);
        protected abstract DRESULT disk_write(BYTE pdrv, ReadOnlySpan<BYTE> buff, LBA_t sector, UINT count);
        protected abstract DRESULT disk_ioctl(BYTE pdrv, BYTE cmd, Span<byte> buff);

        protected static char ff_oem2uni(char oemVal)
        {
            byte b = (byte)oemVal;
            char ch = '\0';
            var chSpan = new Span<char>(ref ch);
            var bSpan = new Span<byte>(ref b);
            if (Encoding.Default.TryGetChars(bSpan, chSpan, out var written) && written == 1)
                return ch;
            else
                return '-';
        }

        protected static char ff_uni2oem(char ch)
        {
            byte b = 0;
            var chSpan = new Span<char>(ref ch);
            var bSpan = new Span<byte>(ref b);
            if (Encoding.Default.TryGetBytes(chSpan, bSpan, out var written) && written == 1)
                return (char)b;
            else
                return '-';
        }

        protected static void memset(Span<byte> span, BYTE value, DWORD size)
            => span.Slice(0, (int)size).Fill(value);

        protected static void memcpy(Span<byte> destSpan, ReadOnlySpan<byte> srcSpan, DWORD size)
            => srcSpan.Slice(0, (int)size).CopyTo(destSpan);

        protected static bool memcmp(ReadOnlySpan<byte> span1, ReadOnlySpan<byte> span2, DWORD size)
            => !span2.Slice(0, (int)size).SequenceEqual(span1.Slice(0, (int)size));

        protected static bool strchr(ReadOnlySpan<char> span, char ch)
            => span.Contains(ch);

        public virtual DWORD get_fattime()
        {
            var utcNow = DateTime.UtcNow;
            return (((DWORD)utcNow.Year - 1980) << 25)
            | ((DWORD)utcNow.Month << 21)
            | ((DWORD)utcNow.Day << 16)
            | (WORD)(utcNow.Hour << 11)
            | (WORD)(utcNow.Minute << 5)
            | (WORD)(utcNow.Second >> 1);
        }

        /* Definitions of volume management */
        /* Filesystem object structure (FATFS) */
        public sealed class FATFS
        {
            private unsafe struct WinArray
            {
                public fixed BYTE ptr[win_size];
                public static implicit operator void*(WinArray array)
                    => &array.ptr[0];
            }

            public const int win_size = 4096;
            public BYTE fs_type; /* Filesystem type (0:blank filesystem object) */
            public BYTE pdrv; /* Volume hosting physical drive */
            public BYTE ldrv; /* Logical drive number (used only when FF_FS_REENTRANT) */
            public BYTE n_fats; /* Number of FATs (1 or 2) */
            public BYTE wflag; /* win[] status (1:dirty) */
            public BYTE fsi_flag; /* Allocation information control (b7:disabled, b0:dirty) */
            public WORD id; /* Volume mount ID */
            public WORD n_rootdir; /* Number of root directory entries (FAT12/16) */
            public WORD csize; /* Cluster size [sectors] */
            public WORD ssize; /* Sector size (512, 1024, 2048 or 4096) */
            public WCHAR[] lfnbuf = null!; /* LFN working buffer */
            public BYTE[] dirbuf = null!; /* Directory entry block scratch pad buffer for exFAT */
            public DWORD last_clst; /* Last allocated cluster (Unknown if >= n_fatent) */
            public DWORD free_clst; /* Number of free clusters (Unknown if >= n_fatent-2) */
            public DWORD n_fatent; /* Number of FAT entries (number of clusters + 2) */
            public DWORD fsize; /* Number of sectors per FAT */
            public LBA_t volbase; /* Volume base sector */
            public LBA_t fatbase; /* FAT base sector */
            public LBA_t dirbase; /* Root directory base sector (FAT12/16) or cluster (FAT32/exFAT) */
            public LBA_t database; /* Data base sector */
            public LBA_t bitbase; /* Allocation bitmap base sector */
            public LBA_t winsect; /* Current sector appearing in the win[] */
            private WinArray _win; /* Disk access window for Directory, FAT (and file data at tiny cfg) */
            public Ptr<BYTE> win => MemoryMarshal.AsBytes(new Span<WinArray>(ref _win));
        
            public long RoundToClusterLength(long bytes)
            {
                var clusterLength = csize * csize;
                if (clusterLength == 0)
                    return bytes;
                else
                    return (bytes + (long)clusterLength - 1) / clusterLength;
            }
        }

        /* Object ID and allocation information (FFOBJID) */
        public struct FFOBJID
        {
            public FATFS fs; /* Pointer to the hosting volume of this object */
            public WORD id; /* Hosting volume's mount ID */
            public BYTE attr; /* Object attribute */
            public BYTE stat; /* Object chain status (b1-0: =0:not contiguous, =2:contiguous, =3:fragmented in this session, b2:sub-directory stretched) */
            public DWORD sclust; /* Object data start cluster (0:no cluster or root directory) */
            public FSIZE_t objsize; /* Object size (valid when sclust != 0) */
            public DWORD n_cont; /* Size of first fragment - 1 (valid when stat == 3) */
            public DWORD n_frag; /* Size of last fragment needs to be written to FAT (valid when not zero) */
            public DWORD c_scl; /* Containing directory start cluster (valid when sclust != 0) */
            public DWORD c_size; /* b31-b8:Size of containing directory, b7-b0: Chain status (valid when c_scl != 0) */
            public DWORD c_ofs; /* Offset in the containing directory (valid when file object and sclust != 0) */
            public bool IsEmpty => fs == null;
        }

        /* File object structure (FIL) */
        public sealed class FIL
        {
            private unsafe struct BufArray
            {
                public fixed BYTE ptr[buf_size]; /* File private data read/write window */
                public static implicit operator void*(BufArray array)
                    => &array.ptr[0];
            }

            public const int buf_size = 4096;
            public FFOBJID obj; /* Object identifier (must be the 1st member to detect invalid object pointer) */
            public BYTE flag; /* File status flags */
            public BYTE err; /* Abort flag (error code) */
            public FSIZE_t fptr; /* File read/write pointer (Zeroed on file open) */
            public DWORD clust; /* Current cluster of fpter (invalid when fptr is 0) */
            public LBA_t sect; /* Sector number appearing in buf[] (0:invalid) */
            public LBA_t dir_sect; /* Sector number containing the directory entry (not used at exFAT) */
            public uint dir_ofs; /* Pointer to the directory entry in the win[] (not used at exFAT) */
            public Ptr<BYTE> dir => new Ptr<BYTE>(obj.fs.win + dir_ofs);
            private BufArray _buf; /* File private data read/write window */
            public Ptr<BYTE> buf => MemoryMarshal.AsBytes(new Span<BufArray>(ref _buf));
            public bool IsEmpty => obj.IsEmpty;
        }

        /* Directory object structure (DIR) */
        public sealed class DIR
        {
            private unsafe struct FnArray
            {
                public fixed BYTE ptr[fn_size]; /* File private data read/write window */
                public static implicit operator void*(FnArray array)
                    => &array.ptr[0];
            }
            public const int fn_size = 12;

            public FFOBJID obj; /* Object identifier */
            public DWORD dptr; /* Current read/write offset */
            public DWORD clust; /* Current cluster */
            public LBA_t sect; /* Current sector (0:Read operation has terminated) */
            public uint dir_ofs; /* Pointer to the directory item in the win[] */
            public Ptr<BYTE> dir => new Ptr<BYTE>(obj.fs.win + dir_ofs);
            private FnArray _fn; /* SFN (in/out) {body[8],ext[3],status[1]} */
            public Ptr<BYTE> fn => MemoryMarshal.AsBytes(new Span<FnArray>(ref _fn));
            public DWORD blk_ofs; /* Offset of current entry block being processed (0xFFFFFFFF:Invalid) */

            public DIR Clone()
                => (DIR)MemberwiseClone();
        }

        /* File information structure (FILINFO) */
        public sealed class FILINFO
        {
            private unsafe struct AltnameArray
            {
                public fixed WCHAR ptr[altname_size];
                public static implicit operator void*(AltnameArray array)
                    => &array.ptr[0];
            }
            private unsafe struct FnameArray
            {
                public fixed WCHAR ptr[fname_size];
                public static implicit operator void*(FnameArray array)
                    => &array.ptr[0];
            }
            public const int altname_size = 12 + 1;
            public const int fname_size = 255 + 1;

            public FSIZE_t fsize; /* File size */
            public WORD fdate; /* Modified date */
            public WORD ftime; /* Modified time */
            public BYTE fattrib; /* File attribute */
            private AltnameArray _altname; /* Alternative file name */
            private FnameArray _fname; /* File name */
            public Ptr<WCHAR> altname => MemoryMarshal.Cast<AltnameArray, WCHAR>(new Span<AltnameArray>(ref _altname));
            public Ptr<WCHAR> fname => MemoryMarshal.Cast<FnameArray, WCHAR>(new Span<FnameArray>(ref _fname));

            public DIR Clone()
                => (DIR)MemberwiseClone();
        }

        public struct PARTITION
        {
            public BYTE pd;    /* Associated physical drive */
            public BYTE pt;    /* Associated partition (0:Auto detect, 1-4:Forced partition) */
        }

        /* Format options (2nd argument of f_mkfs function) */
        public enum FORMAT_TYPE
        {
            FM_FAT = 0x01,
            FM_FAT32 = 0x02,
            FM_EXFAT = 0x04,
            FM_ANY = 0x07,
            FM_SFD = 0x08,
        }

        /* Filesystem type (FATFS.fs_type) */
        public enum FS_TYPE
        {
            FS_FAT12 = 1,
            FS_FAT16 = 2,
            FS_FAT32 = 3,
            FS_EXFAT = 4,
        }

        /* Format parameter structure (MKFS_PARM) */
        public class MKFS_PARM
        {
            public FORMAT_TYPE fmt; /* Format option (FM_FAT, FM_FAT32, FM_EXFAT and FM_SFD) */
            public BYTE n_fat; /* Number of FATs */
            public UINT align; /* Data area alignment (sector) */
            public UINT n_root; /* Number of root directory entries */
            public DWORD au_size; /* Cluster size (byte) */
        }

        /* File function return code (FRESULT) */
        public enum FRESULT : BYTE
        {
            FR_OK = 0, /* (0) Function succeeded */
            FR_DISK_ERR, /* (1) A hard error occurred in the low level disk I/O layer */
            FR_INT_ERR, /* (2) Assertion failed */
            FR_NOT_READY, /* (3) The physical drive does not work */
            FR_NO_FILE, /* (4) Could not find the file */
            FR_NO_PATH, /* (5) Could not find the path */
            FR_INVALID_NAME, /* (6) The path name format is invalid */
            FR_DENIED, /* (7) Access denied due to a prohibited access or directory full */
            FR_EXIST, /* (8) Access denied due to a prohibited access */
            FR_INVALID_OBJECT, /* (9) The file/directory object is invalid */
            FR_WRITE_PROTECTED, /* (10) The physical drive is write protected */
            FR_INVALID_DRIVE, /* (11) The logical drive number is invalid */
            FR_NOT_ENABLED, /* (12) The volume has no work area */
            FR_NO_FILESYSTEM, /* (13) Could not find a valid FAT volume */
            FR_MKFS_ABORTED, /* (14) The f_mkfs function aborted due to some problem */
            FR_TIMEOUT, /* (15) Could not take control of the volume within defined period */
            FR_LOCKED, /* (16) The operation is rejected according to the file sharing policy */
            FR_NOT_ENOUGH_CORE, /* (17) LFN working buffer could not be allocated or given buffer is insufficient in size */
            FR_TOO_MANY_OPEN_FILES, /* (18) Number of open files > FF_FS_LOCK */
            FR_INVALID_PARAMETER /* (19) Given parameter is invalid */
        }

        public enum DRESULT : BYTE
        {
            RES_OK = 0,     /* 0: Successful */
            RES_ERROR,      /* 1: R/W Error */
            RES_WRPRT,      /* 2: Write Protected */
            RES_NOTRDY,     /* 3: Not Ready */
            RES_PARERR      /* 4: Invalid Parameter */
        }

        [Flags]
        public enum MODE : BYTE
        {
            FA_READ = 0x01,
            FA_WRITE = 0x02,
            FA_OPEN_EXISTING = 0x00,
            FA_CREATE_NEW = 0x04,
            FA_CREATE_ALWAYS = 0x08,
            FA_OPEN_ALWAYS = 0x10,
            FA_OPEN_APPEND = 0x30,
        }

        /*--------------------------------------------------------------------------



           Module Private Definitions



        ---------------------------------------------------------------------------*/
        /* Limits and boundaries */
        /* Character code support macros */
        /* Additional file access control and file status flags for internal use */
        /* Additional file attribute bits for internal use */
        /* Name status flags in fn[11] */
        /* exFAT directory entry types */
        /* FatFs refers the FAT structures as simple byte array instead of structure member

        / because the C structure is not binary compatible between different platforms */
        /* Post process on fatal error in the file operations */
        /* Re-entrancy related */
        /* Definitions of logical drive to physical location conversion */
        /* Definitions of sector size */
        /* Timestamp */
        /* File lock controls */
        /* SBCS up-case tables (\x80-\xFF) */
        /* DBCS code range |----- 1st byte -----|  |----------- 2nd byte -----------| */
        /*                  <------>    <------>    <------>    <------>    <------>  */
        /* Macros for table definitions */
        /*--------------------------------------------------------------------------



           Module Private Work Area



        ---------------------------------------------------------------------------*/
        /* Remark: Variables defined here without initial value shall be guaranteed

        /  zero/null at start-up. If not, the linker option or start-up routine is

        /  not compliance with C standard. */
        /*--------------------------------*/
        /* File/Volume controls           */
        /*--------------------------------*/
        public FATFS?[] FatFs = []; /* Pointer to the filesystem objects (logical drives) */
        public WORD Fsid; /* Filesystem mount ID */
        public static BYTE[] GUID_MS_Basic = [0xA2, 0xA0, 0xD0, 0xEB, 0xE5, 0xB9, 0x33, 0x44, 0x87, 0xC0, 0x68, 0xB6, 0xB7, 0x26, 0x99, 0xC7];
        /*--------------------------------*/
        /* LFN/Directory working buffer   */
        /*--------------------------------*/
        public static BYTE[] LfnOfs = [1, 3, 5, 7, 9, 14, 16, 18, 20, 22, 24, 28, 30]; /* FAT: Offset of LFN characters in the directory entry */
        /*--------------------------------*/
        /* Code conversion tables         */
        /*--------------------------------*/
        public readonly static BYTE[] ExCvt = [0x80, 0x9A, 0x45, 0x41, 0x8E, 0x41, 0x8F, 0x80, 0x45, 0x45, 0x45, 0x49, 0x49, 0x49, 0x8E, 0x8F, 0x90, 0x92, 0x92, 0x4F, 0x99, 0x4F, 0x55, 0x55, 0x59, 0x99, 0x9A, 0x9B, 0x9C, 0x9D, 0x9E, 0x9F, 0x41, 0x49, 0x4F, 0x55, 0xA5, 0xA5, 0xA6, 0xA7, 0xA8, 0xA9, 0xAA, 0xAB, 0xAC, 0xAD, 0xAE, 0xAF, 0xB0, 0xB1, 0xB2, 0xB3, 0xB4, 0xB5, 0xB6, 0xB7, 0xB8, 0xB9, 0xBA, 0xBB, 0xBC, 0xBD, 0xBE, 0xBF, 0xC0, 0xC1, 0xC2, 0xC3, 0xC4, 0xC5, 0xC6, 0xC7, 0xC8, 0xC9, 0xCA, 0xCB, 0xCC, 0xCD, 0xCE, 0xCF, 0xD0, 0xD1, 0xD2, 0xD3, 0xD4, 0xD5, 0xD6, 0xD7, 0xD8, 0xD9, 0xDA, 0xDB, 0xDC, 0xDD, 0xDE, 0xDF, 0xE0, 0xE1, 0xE2, 0xE3, 0xE4, 0xE5, 0xE6, 0xE7, 0xE8, 0xE9, 0xEA, 0xEB, 0xEC, 0xED, 0xEE, 0xEF, 0xF0, 0xF1, 0xF2, 0xF3, 0xF4, 0xF5, 0xF6, 0xF7, 0xF8, 0xF9, 0xFA, 0xFB, 0xFC, 0xFD, 0xFE, 0xFF];
        private readonly static string _badchr = "+.,;=[]/*:<>|\\\"?\x7F"; /* [0..16] for FAT, [7..16] for exFAT */
        public readonly static BYTE[] gpt_mbr = [0x00, 0x00, 0x02, 0x00, 0xEE, 0xFE, 0xFF, 0x00, 0x01, 0x00, 0x00, 0x00, 0xFF, 0xFF, 0xFF, 0xFF];

        /*--------------------------------------------------------------------------



           Module Private Functions



        ---------------------------------------------------------------------------*/
        /*-----------------------------------------------------------------------*/
        /* Load/Store multi-byte word in the FAT structure                       */
        /*-----------------------------------------------------------------------*/

        public static WORD ld_word(Span<BYTE> ptr) /*	 Load a 2-byte little-endian word */
            => BinaryPrimitives.ReadUInt16LittleEndian(ptr);

        public static DWORD ld_dword(Span<BYTE> ptr) /* Load a 4-byte little-endian word */
            => BinaryPrimitives.ReadUInt32LittleEndian(ptr);

        public static QWORD ld_qword(Span<BYTE> ptr) /* Load an 8-byte little-endian word */
            => BinaryPrimitives.ReadUInt64LittleEndian(ptr);

        public static void st_word(Span<BYTE> ptr, WORD val) /* Store a 2-byte word in little-endian */
            => BinaryPrimitives.WriteUInt16LittleEndian(ptr, val);

        public static void st_dword(Span<BYTE> ptr, DWORD val) /* Store a 4-byte word in little-endian */
            => BinaryPrimitives.WriteUInt32LittleEndian(ptr, val);
        public static void st_qword(Span<BYTE> ptr, QWORD val) /* Store an 8-byte word in little-endian */
            => BinaryPrimitives.WriteUInt64LittleEndian(ptr, val);
        /*-----------------------------------------------------------------------*/
        /* String functions                                                      */
        /*-----------------------------------------------------------------------*/
        /* Test if the byte is DBC 1st byte */
        public static int dbc_1st(BYTE c)
        {
            if (c != 0) return 0; /* Always false */
            return 0;
        }
        /* Test if the byte is DBC 2nd byte */
        public static int dbc_2nd(BYTE c)
        {
            if (c != 0) return 0; /* Always false */
            return 0;
        }
        /* Get a Unicode code point from the TCHAR string in defined API encodeing */
        public static DWORD tchar2uni( /* Returns a character in UTF-16 encoding (>=0x10000 on surrogate pair, 0xFFFFFFFF on decode error) */
         ref ReadOnlyPtr<TCHAR> str /* Pointer to pointer to TCHAR string in configured encoding */
        )
        {
            DWORD uc;
            ReadOnlyPtr<TCHAR> p = str;
            WCHAR wc;
            uc = (p++).Value; /* Get an encoding unit */
            if (((uc) >= 0xD800 && (uc) <= 0xDFFF))
            { /* Surrogate? */
                wc = (p++).Value; /* Get low surrogate */
                if (!((uc) >= 0xD800 && (uc) <= 0xDBFF) || !((wc) >= 0xDC00 && (wc) <= 0xDFFF)) return 0xFFFFFFFF; /* Wrong surrogate? */
                uc = uc << 16 | wc;
            }
            str = p; /* Next read pointer */
            return uc;
        }
        /* Store a Unicode char in defined API encoding */
        public static UINT put_utf( /* Returns number of encoding units written (0:buffer overflow or wrong encoding) */
         DWORD chr, /* UTF-16 encoded character (Surrogate pair if >=0x10000) */
         Ptr<TCHAR> buf, /* Output buffer */
         UINT szb /* Size of the buffer */
        )
        {
            WCHAR hs, wc;
            hs = (WCHAR)(chr >> 16);
            wc = (WCHAR)chr;
            if (hs == 0)
            { /* Single encoding unit? */
                if (szb < 1 || ((wc) >= 0xD800 && (wc) <= 0xDFFF)) return 0; /* Buffer overflow or wrong code? */
                buf.Value = wc;
                return 1;
            }
            if (szb < 2 || !((hs) >= 0xD800 && (hs) <= 0xDBFF) || !((wc) >= 0xDC00 && (wc) <= 0xDFFF)) return 0; /* Buffer overflow or wrong surrogate? */
            (buf++).Value = hs;
            (buf++).Value = wc;
            return 2;
        }
        /*-----------------------------------------------------------------------*/
        /* Move/Flush disk access window in the filesystem object                */
        /*-----------------------------------------------------------------------*/
        public FRESULT sync_window( /* Returns FR_OK or FR_DISK_ERR */
         FATFS fs /* Filesystem object */
        )
        {
            FRESULT res = FR_OK;
            if (fs.wflag != 0)
            { /* Is the disk access window dirty? */
                if (disk_write(fs.pdrv, fs.win, fs.winsect, 1) == RES_OK)
                { /* Write it back into the volume */
                    fs.wflag = 0; /* Clear window dirty flag */
                    if (fs.winsect - fs.fatbase < fs.fsize)
                    { /* Is it in the 1st FAT? */
                        if (fs.n_fats == 2) disk_write(fs.pdrv, fs.win, fs.winsect + fs.fsize, 1); /* Reflect it to 2nd FAT if needed */
                    }
                }
                else
                {
                    res = FR_DISK_ERR;
                }
            }
            return res;
        }
        public FRESULT move_window( /* Returns FR_OK or FR_DISK_ERR */
         FATFS fs, /* Filesystem object */
         LBA_t sect /* Sector LBA to make appearance in the fs.win[] */
        )
        {
            FRESULT res = FR_OK;
            if (sect != fs.winsect)
            { /* Window offset changed? */
                res = sync_window(fs); /* Flush the window */
                if (res == FR_OK)
                { /* Fill sector window with new data */
                    if (disk_read(fs.pdrv, fs.win, sect, 1) != RES_OK)
                    {
                        sect = unchecked((LBA_t)0 - 1); /* Invalidate window if read data is not valid */
                        res = FR_DISK_ERR;
                    }
                    fs.winsect = sect;
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Synchronize filesystem and data on the storage                        */
        /*-----------------------------------------------------------------------*/
        public FRESULT sync_fs( /* Returns FR_OK or FR_DISK_ERR */
         FATFS fs /* Filesystem object */
        )
        {
            FRESULT res;
            res = sync_window(fs);
            if (res == FR_OK)
            {
                if (fs.fsi_flag == 1)
                { /* Allocation changed? */
                    fs.fsi_flag = 0;
                    if (fs.fs_type == 3)
                    { /* FAT32: Update FSInfo sector */
                        /* Create FSInfo structure */
                        memset(fs.win, 0, FATFS.win_size);
                        st_dword(fs.win + 0, 0x41615252); /* Leading signature */
                        st_dword(fs.win + 484, 0x61417272); /* Structure signature */
                        st_dword(fs.win + 488, fs.free_clst); /* Number of free clusters */
                        st_dword(fs.win + 492, fs.last_clst); /* Last allocated culuster */
                        st_dword(fs.win + 498, 0xAA550000); /* Trailing signature */
                        disk_write(fs.pdrv, fs.win, fs.winsect = fs.volbase + 1, 1); /* Write it into the FSInfo sector (Next to VBR) */
                    }
                    else if (fs.fs_type == 4)
                    { /* exFAT: Update PercInUse field in BPB */
                        if (disk_read(fs.pdrv, fs.win, fs.winsect = fs.volbase, 1) == RES_OK)
                        { /* Load VBR */
                            BYTE perc_inuse = (BYTE)((fs.free_clst <= fs.n_fatent - 2) ? (BYTE)((QWORD)(fs.n_fatent - 2 - fs.free_clst) * 100 / (fs.n_fatent - 2)) : 0xFF); /* Precent in use 0-100 or 0xFF(unknown) */
                            if (fs.win[112] != perc_inuse)
                            { /* Write it back into VBR if needed */
                                fs.win[112] = perc_inuse;
                                disk_write(fs.pdrv, fs.win, fs.winsect, 1);
                            }
                        }
                    }
                }
                /* Make sure that no pending write process in the lower layer */
                if (disk_ioctl(fs.pdrv, 0, default) != RES_OK) res = FR_DISK_ERR;
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Get physical sector number from cluster number                        */
        /*-----------------------------------------------------------------------*/
        static LBA_t clst2sect( /* !=0:Sector number, 0:Failed (invalid cluster#) */
         FATFS fs, /* Filesystem object */
         DWORD clst /* Cluster# to be converted */
        )
        {
            clst -= 2; /* Cluster number is origin from 2 */
            if (clst >= fs.n_fatent - 2) return 0; /* Is it invalid cluster number? */
            return fs.database + (LBA_t)fs.csize * clst; /* Start sector number of the cluster */
        }
        /*-----------------------------------------------------------------------*/
        /* FAT access - Read value of an FAT entry                               */
        /*-----------------------------------------------------------------------*/
        public DWORD get_fat( /* 0xFFFFFFFF:Disk error, 1:Internal error, 2..0x7FFFFFFF:Cluster status */
         ref FFOBJID obj, /* Corresponding object */
         DWORD clst /* Cluster number to get the value */
        )
        {
            UINT wc, bc;
            DWORD val;
            FATFS fs = obj.fs;
            if (clst < 2 || clst >= fs.n_fatent)
            { /* Check if in valid range */
                val = 1; /* Internal error */
            }
            else
            {
                val = 0xFFFFFFFF; /* Default value falls on disk error */
                switch (fs.fs_type)
                {
                    case 1:
                        bc = (UINT)clst; bc += bc / 2;
                        if (move_window(fs, fs.fatbase + (bc / ((fs).ssize))) != FR_OK) break;
                        wc = fs.win[bc++ % ((fs).ssize)]; /* Get 1st byte of the entry */
                        if (move_window(fs, fs.fatbase + (bc / ((fs).ssize))) != FR_OK) break;
                        wc |= (uint)(fs.win[bc % ((fs).ssize)] << 8); /* Merge 2nd byte of the entry */
                        val = (clst & 1) != 0 ? (wc >> 4) : (wc & 0xFFF); /* Adjust bit position */
                        break;
                    case 2:
                        if (move_window(fs, fs.fatbase + (clst / (((QWORD)(fs).ssize) / 2))) != FR_OK) break;
                        val = ld_word(fs.win + clst * 2 % ((fs).ssize)); /* Simple WORD array */
                        break;
                    case 3:
                        if (move_window(fs, fs.fatbase + (clst / (((QWORD)(fs).ssize) / 4))) != FR_OK) break;
                        val = ld_dword(fs.win + clst * 4 % (fs.ssize)) & 0x0FFFFFFF; /* Simple DWORD array but mask out upper 4 bits */
                        break;
                    case 4:
                        if ((obj.objsize != 0 && obj.sclust != 0) || obj.stat == 0)
                        { /* Object except root dir must have valid data length */
                            DWORD cofs = clst - obj.sclust; /* Offset from start cluster */
                            DWORD clen = (DWORD)((LBA_t)((obj.objsize - 1) / (fs.ssize)) / fs.csize); /* Number of clusters - 1 */
                            if (obj.stat == 2 && cofs <= clen)
                            { /* Is it a contiguous chain? */
                                val = (cofs == clen) ? 0x7FFFFFFF : clst + 1; /* No data on the FAT, generate the value */
                                break;
                            }
                            if (obj.stat == 3 && cofs < obj.n_cont)
                            { /* Is it in the 1st fragment? */
                                val = clst + 1; /* Generate the value */
                                break;
                            }
                            if (obj.stat != 2)
                            { /* Get value from FAT if FAT chain is valid */
                                if (obj.n_frag != 0)
                                { /* Is it on the growing edge? */
                                    val = 0x7FFFFFFF; /* Generate EOC */
                                }
                                else
                                {
                                    if (move_window(fs, fs.fatbase + (clst / ((QWORD)(fs.ssize) / 4))) != FR_OK) break;
                                    val = ld_dword(fs.win + clst * 4 % (fs.ssize)) & 0x7FFFFFFF;
                                }
                                break;
                            }
                        }
                        val = 1; /* Internal error */
                        break;
                    default:
                        val = 1; /* Internal error */
                        break;
                }
            }
            return val;
        }
        /*-----------------------------------------------------------------------*/
        /* FAT access - Change value of an FAT entry                             */
        /*-----------------------------------------------------------------------*/
        public FRESULT put_fat( /* FR_OK(0):succeeded, !=0:error */
         FATFS fs, /* Corresponding filesystem object */
         DWORD clst, /* FAT index number (cluster number) to be changed */
         DWORD val /* New value to be set to the entry */
        )
        {
            UINT bc;
            Ptr<BYTE> p;
            FRESULT res = FR_INT_ERR;
            if (clst >= 2 && clst < fs.n_fatent)
            { /* Check if in valid range */
                switch (fs.fs_type)
                {
                    case 1:
                        bc = (UINT)clst; bc += bc / 2; /* bc: byte offset of the entry */
                        res = move_window(fs, fs.fatbase + (bc / ((fs).ssize)));
                        if (res != FR_OK) break;
                        p = fs.win + bc++ % ((fs).ssize);
                        p.Value = (BYTE)((clst & 1) != 0 ? ((p.Value & 0x0F) | ((BYTE)val << 4)) : (BYTE)val); /* Update 1st byte */
                        fs.wflag = 1;
                        res = move_window(fs, fs.fatbase + (bc / ((fs).ssize)));
                        if (res != FR_OK) break;
                        p = fs.win + bc % ((fs).ssize);
                        p.Value = (BYTE)((clst & 1) != 0 ? (BYTE)(val >> 4) : ((p.Value & 0xF0) | ((BYTE)(val >> 8) & 0x0F))); /* Update 2nd byte */
                        fs.wflag = 1;
                        break;
                    case 2:
                        res = move_window(fs, fs.fatbase + (clst / (((QWORD)(fs).ssize) / 2)));
                        if (res != FR_OK) break;
                        st_word(fs.win + clst * 2 % ((fs).ssize), (WORD)val); /* Simple WORD array */
                        fs.wflag = 1;
                        break;
                    case 3:
                    case 4:
                        res = move_window(fs, fs.fatbase + (clst / (((QWORD)fs.ssize) / 4)));
                        if (res != FR_OK) break;
                        if (false || fs.fs_type != 4)
                        {
                            val = (val & 0x0FFFFFFF) | (ld_dword(fs.win + clst * 4 % (fs.ssize)) & 0xF0000000);
                        }
                        st_dword(fs.win + clst * 4 % (fs.ssize), val);
                        fs.wflag = 1;
                        break;
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* exFAT: Accessing FAT and Allocation Bitmap                            */
        /*-----------------------------------------------------------------------*/
        /*--------------------------------------*/
        /* Find a contiguous free cluster block */
        /*--------------------------------------*/
        public DWORD find_bitmap( /* 0:Not found, 2..:Cluster block found, 0xFFFFFFFF:Disk error */
         FATFS fs, /* Filesystem object */
         DWORD clst, /* Cluster number to scan from */
         DWORD ncl /* Number of contiguous clusters to find (1..) */
        )
        {
            BYTE bm, bv;
            UINT i;
            DWORD val, scl, ctr;
            clst -= 2; /* The first bit in the bitmap corresponds to cluster #2 */
            if (clst >= fs.n_fatent - 2) clst = 0;
            scl = val = clst; ctr = 0;
            for (; ; )
            {
                if (move_window(fs, fs.bitbase + val / 8 / ((QWORD)fs.ssize)) != FR_OK) return 0xFFFFFFFF;
                i = val / 8 % (fs.ssize); bm = (BYTE)(1 << ((int)val % 8));
                do
                {
                    do
                    {
                        bv = (BYTE)(fs.win[i] & bm); bm <<= 1; /* Get bit value */
                        if (++val >= fs.n_fatent - 2)
                        { /* Next cluster (with wrap-around) */
                            val = 0; bm = 0; i = (fs.ssize);
                        }
                        if (bv == 0)
                        { /* Is it a free cluster? */
                            if (++ctr == ncl) return scl + 2; /* Check if run length is sufficient for required */
                        }
                        else
                        {
                            scl = val; ctr = 0; /* Encountered a cluster in-use, restart to scan */
                        }
                        if (val == clst) return 0; /* All cluster scanned? */
                    } while (bm != 0);
                    bm = 1;
                } while (++i < (fs.ssize));
            }
        }
        /*----------------------------------------*/
        /* Set/Clear a block of allocation bitmap */
        /*----------------------------------------*/
        public FRESULT change_bitmap(
         FATFS fs, /* Filesystem object */
         DWORD clst, /* Cluster number to change from */
         DWORD ncl, /* Number of clusters to be changed */
         int bv /* bit value to be set (0 or 1) */
        )
        {
            BYTE bm;
            UINT i;
            LBA_t sect;
            clst -= 2; /* The first bit corresponds to cluster #2 */
            sect = fs.bitbase + clst / 8 / (fs.ssize); /* Sector address */
            i = clst / 8 % (fs.ssize); /* Byte offset in the sector */
            bm = (BYTE)(1 << ((int)clst % 8)); /* Bit mask in the byte */
            for (; ; )
            {
                if (move_window(fs, sect++) != FR_OK) return FR_DISK_ERR;
                do
                {
                    do
                    {
                        if (bv == (int)((fs.win[i] & bm) != 0 ? 1 : 0)) return FR_INT_ERR; /* Is the bit expected value? */
                        fs.win[i] ^= bm; /* Flip the bit */
                        fs.wflag = 1;
                        if (--ncl == 0) return FR_OK; /* All bits processed? */
                    } while ((bm <<= 1) != 0); /* Next bit */
                    bm = 1;
                } while (++i < (fs.ssize)); /* Next byte */
                i = 0;
            }
        }
        /*---------------------------------------------*/
        /* Fill the first fragment of the FAT chain    */
        /*---------------------------------------------*/
        public FRESULT fill_first_frag(
         ref FFOBJID obj /* Pointer to the corresponding object */
        )
        {
            FRESULT res;
            DWORD cl, n;
            if (obj.stat == 3)
            { /* Has the object been changed 'fragmented' in this session? */
                for (cl = obj.sclust, n = obj.n_cont; n != 0; cl++, n--)
                { /* Create cluster chain on the FAT */
                    res = put_fat(obj.fs, cl, cl + 1);
                    if (res != FR_OK) return res;
                }
                obj.stat = 0; /* Change status 'FAT chain is valid' */
            }
            return FR_OK;
        }
        /*---------------------------------------------*/
        /* Fill the last fragment of the FAT chain     */
        /*---------------------------------------------*/
        public FRESULT fill_last_frag(
         ref FFOBJID obj, /* Pointer to the corresponding object */
         DWORD lcl, /* Last cluster of the fragment */
         DWORD term /* Value to set the last FAT entry */
        )
        {
            FRESULT res;
            while (obj.n_frag > 0)
            { /* Create the chain of last fragment */
                res = put_fat(obj.fs, lcl - obj.n_frag + 1, (obj.n_frag > 1) ? lcl - obj.n_frag + 2 : term);
                if (res != FR_OK) return res;
                obj.n_frag--;
            }
            return FR_OK;
        }
        /*-----------------------------------------------------------------------*/
        /* FAT handling - Remove a cluster chain                                 */
        /*-----------------------------------------------------------------------*/
        public FRESULT remove_chain( /* FR_OK(0):succeeded, !=0:error */
         ref FFOBJID obj, /* Corresponding object */
         DWORD clst, /* Cluster to remove a chain from */
         DWORD pclst /* Previous cluster of clst (0 if entire chain) */
        )
        {
            FRESULT res = FR_OK;
            DWORD nxt;
            FATFS fs = obj.fs;
            DWORD scl = clst, ecl = clst;
            if (clst < 2 || clst >= fs.n_fatent) return FR_INT_ERR; /* Check if in valid range */
            /* Mark the previous cluster 'EOC' on the FAT if it exists */
            if (pclst != 0 && (false || fs.fs_type != 4 || obj.stat != 2))
            {
                res = put_fat(fs, pclst, 0xFFFFFFFF);
                if (res != FR_OK) return res;
            }
            /* Remove the chain */
            do
            {
                nxt = get_fat(ref obj, clst); /* Get cluster status */
                if (nxt == 0) break; /* Empty cluster? */
                if (nxt == 1) return FR_INT_ERR; /* Internal error? */
                if (nxt == 0xFFFFFFFF) return FR_DISK_ERR; /* Disk error? */
                if (false || fs.fs_type != 4)
                {
                    res = put_fat(fs, clst, 0); /* Mark the cluster 'free' on the FAT */
                    if (res != FR_OK) return res;
                }
                if (fs.free_clst < fs.n_fatent - 2)
                { /* Update allocation information if it is valid */
                    fs.free_clst++;
                    fs.fsi_flag |= 1;
                }
                if (ecl + 1 == nxt)
                { /* Is next cluster contiguous? */
                    ecl = nxt;
                }
                else
                { /* End of contiguous cluster block */
                    if (fs.fs_type == 4)
                    {
                        res = change_bitmap(fs, scl, ecl - scl + 1, 0); /* Mark the cluster block 'free' on the bitmap */
                        if (res != FR_OK) return res;
                    }
                    scl = ecl = nxt;
                }
                clst = nxt; /* Next cluster */
            } while (clst < fs.n_fatent); /* Repeat until the last link */
            /* Some post processes for chain status */
            if (fs.fs_type == 4)
            {
                if (pclst == 0)
                { /* Has the entire chain been removed? */
                    obj.stat = 0; /* Change the chain status 'initial' */
                }
                else
                {
                    if (obj.stat == 0)
                    { /* Is it a fragmented chain from the beginning of this session? */
                        clst = obj.sclust; /* Follow the chain to check if it gets contiguous */
                        while (clst != pclst)
                        {
                            nxt = get_fat(ref obj, clst);
                            if (nxt < 2) return FR_INT_ERR;
                            if (nxt == 0xFFFFFFFF) return FR_DISK_ERR;
                            if (nxt != clst + 1) break; /* Not contiguous? */
                            clst++;
                        }
                        if (clst == pclst)
                        { /* Has the chain got contiguous again? */
                            obj.stat = 2; /* Change the chain status 'contiguous' */
                        }
                    }
                    else
                    {
                        if (obj.stat == 3 && pclst >= obj.sclust && pclst <= obj.sclust + obj.n_cont)
                        { /* Was the chain fragmented in this session and got contiguous again? */
                            obj.stat = 2; /* Change the chain status 'contiguous' */
                        }
                    }
                }
            }
            return FR_OK;
        }

        /*-----------------------------------------------------------------------*/
        /* FAT handling - Stretch a chain or Create a new chain                  */
        /*-----------------------------------------------------------------------*/
        public DWORD create_chain( /* 0:No free cluster, 1:Internal error, 0xFFFFFFFF:Disk error, >=2:New cluster# */
         ref FFOBJID obj, /* Corresponding object */
         DWORD clst /* Cluster# to stretch, 0:Create a new chain */
        )
        {
            DWORD cs, ncl, scl;
            FRESULT res;
            FATFS fs = obj.fs;
            if (clst == 0)
            { /* Create a new chain */
                scl = fs.last_clst; /* Suggested cluster to start to find */
                if (scl == 0 || scl >= fs.n_fatent) scl = 1;
            }
            else
            { /* Stretch a chain */
                cs = get_fat(ref obj, clst); /* Check the cluster status */
                if (cs < 2) return 1; /* Test for insanity */
                if (cs == 0xFFFFFFFF) return cs; /* Test for disk error */
                if (cs < fs.n_fatent) return cs; /* It is already followed by next cluster */
                scl = clst; /* Cluster to start to find */
            }
            if (fs.free_clst == 0) return 0; /* No free cluster */
            if (fs.fs_type == 4)
            { /* On the exFAT volume */
                ncl = find_bitmap(fs, scl, 1); /* Find a free cluster */
                if (ncl == 0 || ncl == 0xFFFFFFFF) return ncl; /* No free cluster or hard error? */
                res = change_bitmap(fs, ncl, 1, 1); /* Mark the cluster 'in use' */
                if (res == FR_INT_ERR) return 1;
                if (res == FR_DISK_ERR) return 0xFFFFFFFF;
                if (clst == 0)
                { /* Is it a new chain? */
                    obj.stat = 2; /* Set status 'contiguous' */
                }
                else
                { /* It is a stretched chain */
                    if (obj.stat == 2 && ncl != scl + 1)
                    { /* Is the chain got fragmented? */
                        obj.n_cont = scl - obj.sclust; /* Set size of the contiguous part */
                        obj.stat = 3; /* Change status 'just fragmented' */
                    }
                }
                if (obj.stat != 2)
                { /* Is the file non-contiguous? */
                    if (ncl == clst + 1)
                    { /* Is the cluster next to previous one? */
                        obj.n_frag = obj.n_frag != 0 ? obj.n_frag + 1 : 2; /* Increment size of last framgent */
                    }
                    else
                    { /* New fragment */
                        if (obj.n_frag == 0) obj.n_frag = 1;
                        res = fill_last_frag(ref obj, clst, ncl); /* Fill last fragment on the FAT and link it to new one */
                        if (res == FR_OK) obj.n_frag = 1;
                    }
                }
            }
            else
            { /* On the FAT/FAT32 volume */
                ncl = 0;
                if (scl == clst)
                { /* Stretching an existing chain? */
                    ncl = scl + 1; /* Test if next cluster is free */
                    if (ncl >= fs.n_fatent) ncl = 2;
                    cs = get_fat(ref obj, ncl); /* Get next cluster status */
                    if (cs == 1 || cs == 0xFFFFFFFF) return cs; /* Test for error */
                    if (cs != 0)
                    { /* Not free? */
                        cs = fs.last_clst; /* Start at suggested cluster if it is valid */
                        if (cs >= 2 && cs < fs.n_fatent) scl = cs;
                        ncl = 0;
                    }
                }
                if (ncl == 0)
                { /* The new cluster cannot be contiguous and find another fragment */
                    ncl = scl; /* Start cluster */
                    for (; ; )
                    {
                        ncl++; /* Next cluster */
                        if (ncl >= fs.n_fatent)
                        { /* Check wrap-around */
                            ncl = 2;
                            if (ncl > scl) return 0; /* No free cluster found? */
                        }
                        cs = get_fat(ref obj, ncl); /* Get the cluster status */
                        if (cs == 0) break; /* Found a free cluster? */
                        if (cs == 1 || cs == 0xFFFFFFFF) return cs; /* Test for error */
                        if (ncl == scl) return 0; /* No free cluster found? */
                    }
                }
                res = put_fat(fs, ncl, 0xFFFFFFFF); /* Mark the new cluster 'EOC' */
                if (res == FR_OK && clst != 0)
                {
                    res = put_fat(fs, clst, ncl); /* Link it from the previous one if needed */
                }
            }
            if (res == FR_OK)
            { /* Update allocation information if the function succeeded */
                fs.last_clst = ncl;
                if (fs.free_clst > 0 && fs.free_clst <= fs.n_fatent - 2)
                {
                    fs.free_clst--;
                    fs.fsi_flag |= 1;
                }
            }
            else
            {
                ncl = (res == FR_DISK_ERR) ? 0xFFFFFFFF : 1; /* Failed. Generate error status */
            }
            return ncl; /* Return new cluster number or error status */
        }
        /*-----------------------------------------------------------------------*/
        /* Directory handling - Fill a cluster with zeros                        */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_clear( /* Returns FR_OK or FR_DISK_ERR */
         FATFS fs, /* Filesystem object */
         DWORD clst /* Directory table to clear */
        )
        {
            LBA_t sect;
            UINT n, szb;
            BYTE[]? ibufRent = null;
            Ptr<BYTE> ibuf;
            if (sync_window(fs) != FR_OK) return FR_DISK_ERR; /* Flush disk access window */
            sect = clst2sect(fs, clst); /* Top of the cluster */
            fs.winsect = sect; /* Set window to top of the cluster */
            memset(fs.win, 0, FATFS.win_size); /* Clear window buffer */
            /* Allocate a temporary buffer */
            for (szb = ((DWORD)fs.csize * (fs.ssize) >= 0x8000) ? 0x8000U : fs.csize * ((UINT)fs.ssize), ibuf = null!; szb > (fs.ssize) && (ibuf = ibufRent = ArrayPool<byte>.Shared.Rent((int)szb)) == null!; szb /= 2) ;
            if (szb > (fs.ssize))
            { /* Buffer allocated? */
                memset(ibuf, 0, szb);
                szb /= (fs.ssize); /* Bytes -> Sectors */
                for (n = 0; n < fs.csize && disk_write(fs.pdrv, ibuf, sect + n, szb) == RES_OK; n += szb) ; /* Fill the cluster with 0 */
                ArrayPool<byte>.Shared.Return(ibufRent!);
            }
            else
            {
                ibuf = fs.win; szb = 1; /* Use window buffer (many single-sector writes may take a time) */
                for (n = 0; n < fs.csize && disk_write(fs.pdrv, ibuf, sect + n, szb) == RES_OK; n += szb) ; /* Fill the cluster with 0 */
            }
            return (n == fs.csize) ? FR_OK : FR_DISK_ERR;
        }
        /*-----------------------------------------------------------------------*/
        /* Directory handling - Set directory index                              */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_sdi( /* FR_OK(0):succeeded, !=0:error */
         DIR dp, /* Pointer to directory object */
         DWORD ofs /* Offset of directory table */
        )
        {
            DWORD csz, clst;
            FATFS fs = dp.obj.fs;
            if (ofs >= (DWORD)((true && fs.fs_type == 4) ? 0x10000000U : 0x200000U) || (ofs % 32) != 0)
            { /* Check range of offset and alignment */
                return FR_INT_ERR;
            }
            dp.dptr = ofs; /* Set current offset */
            clst = dp.obj.sclust; /* Table start cluster (0:root) */
            if (clst == 0 && fs.fs_type >= 3)
            { /* Replace cluster# 0 with root cluster# */
                clst = (DWORD)fs.dirbase;
                if (true) dp.obj.stat = 0; /* exFAT: Root dir has an FAT chain */
            }
            if (clst == 0)
            { /* Static table (root-directory on the FAT volume) */
                if (ofs / 32 >= fs.n_rootdir) return FR_INT_ERR; /* Is index out of range? */
                dp.sect = fs.dirbase;
            }
            else
            { /* Dynamic table (sub-directory or root-directory on the FAT32/exFAT volume) */
                csz = (DWORD)fs.csize * (fs.ssize); /* Bytes per cluster */
                while (ofs >= csz)
                { /* Follow cluster chain */
                    clst = get_fat(ref dp.obj, clst); /* Get next cluster */
                    if (clst == 0xFFFFFFFF) return FR_DISK_ERR; /* Disk error */
                    if (clst < 2 || clst >= fs.n_fatent) return FR_INT_ERR; /* Reached to end of table or internal error */
                    ofs -= csz;
                }
                dp.sect = clst2sect(fs, clst);
            }
            dp.clust = clst; /* Current cluster# */
            if (dp.sect == 0) return FR_INT_ERR;
            dp.sect += ofs / (fs.ssize); /* Sector# of the directory entry */
            dp.dir_ofs = (ofs % (fs.ssize)); /* Pointer to the entry in the win[] */
            return FR_OK;
        }
        /*-----------------------------------------------------------------------*/
        /* Directory handling - Move directory table index next                  */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_next( /* FR_OK(0):succeeded, FR_NO_FILE:End of table, FR_DENIED:Could not stretch */
         DIR dp, /* Pointer to the directory object */
         int stretch /* 0: Do not stretch table, 1: Stretch table if needed */
        )
        {
            DWORD ofs, clst;
            FATFS fs = dp.obj.fs;
            ofs = dp.dptr + 32; /* Next entry */
            if (ofs >= (DWORD)((true && fs.fs_type == 4) ? 0x10000000 : 0x200000)) dp.sect = 0; /* Disable it if the offset reached the max value */
            if (dp.sect == 0) return FR_NO_FILE; /* Report EOT if it has been disabled */
            if (ofs % (fs.ssize) == 0)
            { /* Sector changed? */
                dp.sect++; /* Next sector */
                if (dp.clust == 0)
                { /* Static table */
                    if (ofs / 32 >= fs.n_rootdir)
                    { /* Report EOT if it reached end of static table */
                        dp.sect = 0; return FR_NO_FILE;
                    }
                }
                else
                { /* Dynamic table */
                    if ((ofs / (fs.ssize) & (fs.csize - 1)) == 0)
                    { /* Cluster changed? */
                        clst = get_fat(ref dp.obj, dp.clust); /* Get next cluster */
                        if (clst <= 1) return FR_INT_ERR; /* Internal error */
                        if (clst == 0xFFFFFFFF) return FR_DISK_ERR; /* Disk error */
                        if (clst >= fs.n_fatent)
                        { /* It reached end of dynamic table */
                            if (stretch == 0)
                            { /* If no stretch, report EOT */
                                dp.sect = 0; return FR_NO_FILE;
                            }
                            clst = create_chain(ref dp.obj, dp.clust); /* Allocate a cluster */
                            if (clst == 0) return FR_DENIED; /* No free cluster */
                            if (clst == 1) return FR_INT_ERR; /* Internal error */
                            if (clst == 0xFFFFFFFF) return FR_DISK_ERR; /* Disk error */
                            if (dir_clear(fs, clst) != FR_OK) return FR_DISK_ERR; /* Clean up the stretched table */
                            if (true) dp.obj.stat |= 4; /* exFAT: The directory has been stretched */
                        }
                        dp.clust = clst; /* Initialize data for new cluster */
                        dp.sect = clst2sect(fs, clst);
                    }
                }
            }
            dp.dptr = ofs; /* Current entry */
            dp.dir_ofs = ofs % (fs.ssize); /* Pointer to the entry in the win[] */
            return FR_OK;
        }
        /*-----------------------------------------------------------------------*/
        /* Directory handling - Reserve a block of directory entries             */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_alloc( /* FR_OK(0):succeeded, !=0:error */
         DIR dp, /* Pointer to the directory object */
         UINT n_ent /* Number of contiguous entries to allocate */
        )
        {
            FRESULT res;
            UINT n;
            FATFS fs = dp.obj.fs;
            res = dir_sdi(dp, 0);
            if (res == FR_OK)
            {
                n = 0;
                do
                {
                    res = move_window(fs, dp.sect);
                    if (res != FR_OK) break;
                    if (((fs.fs_type == 4) ? (int)((dp.dir[0] & 0x80) == 0 ? 1 : 0) : (int)((dp.dir[0] == 0xE5 || dp.dir[0] == 0) ? 1 : 0)) != 0)
                    { /* Is the entry free? */
                        if (++n == n_ent) break; /* Is a block of contiguous free entries found? */
                    }
                    else
                    {
                        n = 0; /* Not a free entry, restart to search */
                    }
                    res = dir_next(dp, 1); /* Next entry with table stretch enabled */
                } while (res == FR_OK);
            }
            if (res == FR_NO_FILE) res = FR_DENIED; /* No directory entry to allocate */
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* FAT: Directory handling - Load/Store start cluster number             */
        /*-----------------------------------------------------------------------*/
        public DWORD ld_clust( /* Returns the top cluster value of the SFN entry */
         FATFS fs, /* Pointer to the fs object */
 Ptr<BYTE> dir /* Pointer to the key entry */
        )
        {
            DWORD cl;
            cl = ld_word(dir + 26);
            if (fs.fs_type == 3)
            {
                cl |= (DWORD)ld_word(dir + 20) << 16;
            }
            return cl;
        }
        public void st_clust(
         FATFS fs, /* Pointer to the fs object */
         Ptr<BYTE> dir, /* Pointer to the key entry */
         DWORD cl /* Value to be set */
        )
        {
            st_word(dir + 26, (WORD)cl);
            if (fs.fs_type == 3)
            {
                st_word(dir + 20, (WORD)(cl >> 16));
            }
        }
        /*--------------------------------------------------------*/
        /* FAT-LFN: Compare a part of file name with an LFN entry */
        /*--------------------------------------------------------*/
        public static int cmp_lfn( /* 1:matched, 0:not matched */
 Ptr<WCHAR> lfnbuf, /* Pointer to the LFN to be compared */
 Ptr<BYTE> dir /* Pointer to the LFN entry */
)
        {
            UINT ni, di;
            WCHAR pchr, chr;
            if (ld_word(dir + 26) != 0) return 0; /* Check if LDIR_FstClusLO is 0 */
            ni = (UINT)((dir[0] & 0x3F) - 1) * 13; /* Offset in the name to be compared */
            for (pchr = (WCHAR)1, di = 0; di < 13; di++)
            { /* Process all characters in the entry */
                chr = (WCHAR)ld_word(dir + LfnOfs[di]); /* Pick a character from the entry */
                if (pchr != 0)
                {
                    if (ni >= 255 + 1 || char.ToUpper((char)chr) != char.ToUpper((char)lfnbuf[ni++]))
                    { /* Compare it with name */
                        return 0; /* Not matched */
                    }
                    pchr = chr;
                }
                else
                {
                    if (chr != 0xFFFF) return 0; /* Check filler */
                }
            }
            if ((dir[0] & 0x40) != 0 && pchr != 0 && lfnbuf[ni] != 0) return 0; /* Last name segment matched but different length */
            return 1; /* The part of LFN matched */
        }
        /*-----------------------------------------------------*/
        /* FAT-LFN: Pick a part of file name from an LFN entry */
        /*-----------------------------------------------------*/
        public static int pick_lfn( /* 1:succeeded, 0:buffer overflow or invalid LFN entry */
         Ptr<WCHAR> lfnbuf, /* Pointer to the name buffer to be stored */
         Ptr<BYTE> dir /* Pointer to the LFN entry */
        )
        {
            UINT ni, di;
            WCHAR pchr, chr;
            if (ld_word(dir + 26) != 0) return 0; /* Check if LDIR_FstClusLO is 0 */
            ni = (UINT)((dir[0] & ~0x40) - 1) * 13; /* Offset in the name buffer */
            for (pchr = (WCHAR)1, di = 0; di < 13; di++)
            { /* Process all characters in the entry */
                chr = (WCHAR)ld_word(dir + LfnOfs[di]); /* Pick a character from the entry */
                if (pchr != 0)
                {
                    if (ni >= 255 + 1) return 0; /* Buffer overflow? */
                    lfnbuf[ni++] = pchr = chr; /* Store it */
                }
                else
                {
                    if (chr != 0xFFFF) return 0; /* Check filler */
                }
            }
            if ((dir[0] & 0x40) != 0 && pchr != 0)
            { /* Put terminator if it is the last LFN part and not terminated */
                if (ni >= 255 + 1) return 0; /* Buffer overflow? */
                lfnbuf[ni] = (WCHAR)0;
            }
            return 1; /* The part of LFN is valid */
        }
        /*-----------------------------------------*/
        /* FAT-LFN: Create an entry of LFN entries */
        /*-----------------------------------------*/
        public static void put_lfn(
         Ptr<WCHAR> lfn, /* Pointer to the LFN */
         Ptr<BYTE> dir, /* Pointer to the LFN entry to be created */
         BYTE ord, /* LFN order (1-20) */
         BYTE sum /* Checksum of the corresponding SFN */
        )
        {
            UINT ni, di;
            WCHAR chr;
            dir[13] = sum; /* Set checksum */
            dir[11] = 0x0F; /* Set attribute */
            dir[12] = 0;
            st_word(dir + 26, 0);
            ni = (UINT)(ord - 1) * 13; /* Offset in the name */
            di = chr = (WCHAR)0;
            do
            { /* Fill the directory entry */
                if (chr != 0xFFFF) chr = lfn[ni++]; /* Get an effective character */
                st_word(dir + LfnOfs[di], chr); /* Set it */
                if (chr == 0) chr = (WCHAR)0xFFFF; /* Padding characters after the terminator */
            } while (++di < 13);
            if (chr == 0xFFFF || lfn[ni] == 0) ord |= 0x40; /* Last LFN part is the start of an enrty set */
            dir[0] = ord; /* Set order in the entry set */
        }
        /*-----------------------------------------------------------------------*/
        /* FAT-LFN: Create a Numbered SFN                                        */
        /*-----------------------------------------------------------------------*/
        public static void gen_numname(
         Ptr<BYTE> dst, /* Pointer to the buffer to store numbered SFN */
         Ptr<BYTE> src, /* Pointer to SFN in directory form */
         Ptr<WCHAR> lfn, /* Pointer to LFN */
         UINT seq /* Sequence number */
        )
        {
            Span<BYTE> nsSpan = stackalloc BYTE[8];
            Ptr<BYTE> ns = nsSpan;
            BYTE c;
            UINT i, j;
            WCHAR wc;
            DWORD crc_sreg;
            memcpy(dst, src, 11); /* Prepare the SFN to be modified */
            if (seq > 5)
            { /* In case of many collisions, generate a hash number instead of sequential number */
                crc_sreg = seq;
                while (lfn.Value != 0)
                { /* Create a CRC value as a hash of LFN */
                    wc = (lfn++).Value;
                    for (i = 0; i < 16; i++)
                    {
                        crc_sreg = (UINT)((crc_sreg << 1) + (wc & 1));
                        wc >>= 1;
                        if ((crc_sreg & 0x10000) != 0) crc_sreg ^= 0x11021;
                    }
                }
                seq = (UINT)crc_sreg;
            }
            /* Make suffix (~ + hexdecimal) */
            i = 7;
            do
            {
                c = (BYTE)((seq % 16) + '0'); seq /= 16;
                if (c > '9') c += 7;
                ns[i--] = c;
            } while (i != 0 && seq != 0);
            ns[i] = (byte)'~';
            /* Append the suffix to the SFN body */
            for (j = 0; j < i && dst[j] != ' '; j++)
            { /* Find the offset to append */
                if (dbc_1st(dst[j]) != 0)
                { /* To avoid DBC break up */
                    if (j == i - 1) break;
                    j++;
                }
            }
            do
            { /* Append the suffix */
                dst[j++] = (i < 8) ? ns[i++] : (BYTE)' ';
            } while (j < 8);
        }
        /*-----------------------------------------------------------------------*/
        /* FAT-LFN: Calculate checksum of an SFN entry                           */
        /*-----------------------------------------------------------------------*/
        public static BYTE sum_sfn(
         Ptr<BYTE> dir /* Pointer to the SFN entry */
        )
        {
            BYTE sum = 0;
            UINT n = 11;
            do
            {
                sum = (BYTE)((sum >> 1) + (sum << 7) + (dir++).Value);
            } while ((--n) != 0);
            return sum;
        }
        /*-----------------------------------------------------------------------*/
        /* exFAT: Checksum                                                       */
        /*-----------------------------------------------------------------------*/
        public static WORD xdir_sum( /* Get checksum of the directoly entry block */
         Ptr<BYTE> dir /* Directory entry block to be calculated */
        )
        {
            UINT i, szblk;
            WORD sum;
            szblk = ((UINT)dir[1] + 1) * 32; /* Number of bytes of the entry block */
            for (i = sum = 0; i < szblk; i++)
            {
                if (i == 2)
                { /* Skip 2-byte sum field */
                    i++;
                }
                else
                {
                    sum = (WORD)(((sum & 1) != 0 ? 0x8000 : 0) + (sum >> 1) + dir[i]);
                }
            }
            return sum;
        }
        public static WORD xname_sum( /* Get check sum (to be used as hash) of the file name */
         Ptr<WCHAR> name /* File name to be calculated */
        )
        {
            WCHAR chr;
            WORD sum = 0;
            while ((chr = (name++).Value) != 0)
            {
                chr = (WCHAR)char.ToUpper((char)chr); /* File name needs to be up-case converted */
                sum = (WORD)(((sum & 1) != 0 ? 0x8000 : 0) + (sum >> 1) + (chr & 0xFF));
                sum = (WORD)(((sum & 1) != 0 ? 0x8000 : 0) + (sum >> 1) + (chr >> 8));
            }
            return sum;
        }
        public static DWORD xsum32( /* Returns 32-bit checksum */
         BYTE dat, /* Byte to be calculated (byte-by-byte processing) */
         DWORD sum /* Previous sum value */
        )
        {
            sum = ((sum & 1) != 0 ? 0x80000000 : 0) + (sum >> 1) + dat;
            return sum;
        }
        /*------------------------------------*/
        /* exFAT: Get a directory entry block */
        /*------------------------------------*/
        public FRESULT load_xdir( /* FR_INT_ERR: invalid entry block */
         DIR dp /* Reading directory object pointing top of the entry block to load */
        )
        {
            FRESULT res;
            UINT i, sz_ent;
            Ptr<BYTE> dirb = dp.obj.fs.dirbuf; /* Pointer to the on-memory directory entry block 85+C0+C1s */
            /* Load file-directory entry */
            res = move_window(dp.obj.fs, dp.sect);
            if (res != FR_OK) return res;
            if (dp.dir[0] != 0x85) return FR_INT_ERR; /* Invalid order? */
            memcpy(dirb + 0 * 32, dp.dir, 32);
            sz_ent = ((UINT)dirb[1] + 1) * 32; /* Size of this entry block */
            if (sz_ent < 3 * 32 || sz_ent > 19 * 32) return FR_INT_ERR; /* Invalid block size? */
            /* Load stream extension entry */
            res = dir_next(dp, 0);
            if (res == FR_NO_FILE) res = FR_INT_ERR; /* It cannot be */
            if (res != FR_OK) return res;
            res = move_window(dp.obj.fs, dp.sect);
            if (res != FR_OK) return res;
            if (dp.dir[0] != 0xC0) return FR_INT_ERR; /* Invalid order? */
            memcpy(dirb + 1 * 32, dp.dir, 32);
            if (((dirb[35] + 44U) / 15 * 32) > sz_ent) return FR_INT_ERR; /* Invalid block size for the name? */
            /* Load file name entries */
            i = 2 * 32; /* Name offset to load */
            do
            {
                res = dir_next(dp, 0);
                if (res == FR_NO_FILE) res = FR_INT_ERR; /* It cannot be */
                if (res != FR_OK) return res;
                res = move_window(dp.obj.fs, dp.sect);
                if (res != FR_OK) return res;
                if (dp.dir[0] != 0xC1) return FR_INT_ERR; /* Invalid order? */
                if (i < ((255 + 44U) / 15 * 32)) memcpy(dirb + i, dp.dir, 32); /* Load name entries only if the object is accessible */
            } while ((i += 32) < sz_ent);
            /* Sanity check (do it for only accessible object) */
            if (i <= ((255 + 44U) / 15 * 32))
            {
                if (xdir_sum(dirb) != ld_word(dirb + 2)) return FR_INT_ERR;
            }
            return FR_OK;
        }
        /*------------------------------------------------------------------*/
        /* exFAT: Initialize object allocation info with loaded entry block */
        /*------------------------------------------------------------------*/
        public static void init_alloc_info(
         FATFS fs, /* Filesystem object */
         ref FFOBJID obj /* Object allocation information to be initialized */
        )
        {
            obj.sclust = ld_dword(fs.dirbuf.AsSpan(52)); /* Start cluster */
            obj.objsize = ld_qword(fs.dirbuf.AsSpan(56)); /* Size */
            obj.stat = (BYTE)(fs.dirbuf[33] & 2); /* Allocation status */
            obj.n_frag = 0; /* No last fragment info */
        }
        /*------------------------------------------------*/
        /* exFAT: Load the object's directory entry block */
        /*------------------------------------------------*/
        public FRESULT load_obj_xdir(
         DIR dp, /* Blank directory object to be used to access containing directory */
         ref FFOBJID obj /* Object with its containing directory information */
        )
        {
            FRESULT res;
            /* Open object containing directory */
            dp.obj.fs = obj.fs;
            dp.obj.sclust = obj.c_scl;
            dp.obj.stat = (BYTE)obj.c_size;
            dp.obj.objsize = obj.c_size & 0xFFFFFF00;
            dp.obj.n_frag = 0;
            dp.blk_ofs = obj.c_ofs;
            res = dir_sdi(dp, dp.blk_ofs); /* Goto object's entry block */
            if (res == FR_OK)
            {
                res = load_xdir(dp); /* Load the object's entry block */
            }
            return res;
        }
        /*----------------------------------------*/
        /* exFAT: Store the directory entry block */
        /*----------------------------------------*/
        public FRESULT store_xdir(
         DIR dp /* Pointer to the directory object */
        )
        {
            FRESULT res;
            UINT nent;
            Ptr<BYTE> dirb = dp.obj.fs.dirbuf; /* Pointer to the entry set 85+C0+C1s */
            st_word(dirb + 2, xdir_sum(dirb)); /* Create check sum */
            /* Store the entry set to the directory */
            nent = ((UINT)(dirb[1] + 1)); /* Number of entries */
            res = dir_sdi(dp, dp.blk_ofs); /* Top of the entry set */
            while (res == FR_OK)
            {
                /* Set an entry to the directory */
                res = move_window(dp.obj.fs, dp.sect);
                if (res != FR_OK) break;
                memcpy(dp.dir, dirb, 32);
                dp.obj.fs.wflag = 1;
                if (--nent == 0) break; /* All done? */
                dirb += 32;
                res = dir_next(dp, 0); /* Next entry */
            }
            return (res == FR_OK || res == FR_DISK_ERR) ? res : FR_INT_ERR;
        }
        /*-------------------------------------------*/
        /* exFAT: Create a new directory entry block */
        /*-------------------------------------------*/
        public void create_xdir(
         Ptr<BYTE> dirb, /* Pointer to the directory entry block buffer */
         Ptr<WCHAR> lfn /* Pointer to the object name */
        )
        {
            UINT i;
            BYTE n_c1, nlen;
            WCHAR chr;
            /* Create file-directory and stream-extension entry (1st and 2nd entry) */
            memset(dirb, 0, 2 * 32);
            dirb[0 * 32 + 0] = 0x85;
            dirb[1 * 32 + 0] = 0xC0;
            /* Create file name entries (3rd enrty and follows) */
            i = 32 * 2; /* Top of file name entries */
            nlen = n_c1 = 0; chr = (WCHAR)1;
            do
            {
                dirb[i++] = 0xC1; dirb[i++] = 0;
                do
                { /* Fill name field */
                    if (chr != 0 && (chr = lfn[nlen]) != 0) nlen++; /* Get a character if exist */
                    st_word(dirb + i, chr); /* Store it */
                    i += 2;
                } while (i % 32 != 0);
                n_c1++;
            } while (lfn[nlen] != 0); /* Fill next C1 entry if any char follows */
            dirb[35] = nlen; /* Set name length */
            dirb[1] = (BYTE)(1 + n_c1); /* Set secondary count (C0 + C1s) */
            st_word(dirb + 36, xname_sum(lfn)); /* Set name hash */
        }
        /*-----------------------------------------------------------------------*/
        /* Read an object from the directory                                     */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_read(
         DIR dp, /* Pointer to the directory object */
         int vol /* Filtered by 0:file/directory or 1:volume label */
        )
        {
            FRESULT res = FR_NO_FILE;
            FATFS fs = dp.obj.fs;
            BYTE attr, b;
            BYTE ord = 0xFF, sum = 0xFF;
            while (dp.sect != 0)
            {
                res = move_window(fs, dp.sect);
                if (res != FR_OK) break;
                b = dp.dir[0]; /* Test for the entry type */
                if (b == 0)
                {
                    res = FR_NO_FILE; break; /* Reached to end of the directory */
                }
                if (fs.fs_type == 4)
                { /* On the exFAT volume */
                    if (true && vol != 0)
                    {
                        if (b == 0x83) break; /* Volume label entry? */
                    }
                    else
                    {
                        if (b == 0x85)
                        { /* Start of the file entry block? */
                            dp.blk_ofs = dp.dptr; /* Get location of the block */
                            res = load_xdir(dp); /* Load the entry block */
                            if (res == FR_OK)
                            {
                                dp.obj.attr = (BYTE)(fs.dirbuf[4] & 0x3F); /* Get attribute */
                            }
                            break;
                        }
                    }
                }
                else
                { /* On the FAT/FAT32 volume */
                    dp.obj.attr = attr = (BYTE)(dp.dir[11] & 0x3F); /* Get attribute */
                    if (b == 0xE5 || b == '.' || (int)((attr & ~0x20) == 0x08 ? 1 : 0) != vol)
                    { /* An entry without valid data */
                        ord = 0xFF;
                    }
                    else
                    {
                        if (attr == 0x0F)
                        { /* An LFN entry is found */
                            if ((b & 0x40) != 0)
                            { /* Is it start of an LFN sequence? */
                                sum = dp.dir[13];
                                b &= unchecked((BYTE)~0x40); ord = b;
                                dp.blk_ofs = dp.dptr;
                            }
                            /* Check LFN validity and capture it */
                            ord = (BYTE)((b == ord && sum == dp.dir[13] && pick_lfn(fs.lfnbuf, dp.dir) != 0) ? ord - 1 : 0xFF);
                        }
                        else
                        { /* An SFN entry is found */
                            if (ord != 0 || sum != sum_sfn(dp.dir))
                            { /* Is there a valid LFN? */
                                dp.blk_ofs = 0xFFFFFFFF; /* It has no LFN. */
                            }
                            break;
                        }
                    }
                }
                res = dir_next(dp, 0); /* Next entry */
                if (res != FR_OK) break;
            }
            if (res != FR_OK) dp.sect = 0; /* Terminate the read operation on error or EOT */
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Directory handling - Find an object in the directory                  */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_find( /* FR_OK(0):succeeded, !=0:error */
         DIR dp /* Pointer to the directory object with the file name */
        )
        {
            FRESULT res;
            FATFS fs = dp.obj.fs;
            BYTE c;
            BYTE a, ord, sum;

            res = dir_sdi(dp, 0); /* Rewind directory object */
            if (res != FR_OK) return res;
            if (fs.fs_type == 4)
            { /* On the exFAT volume */
                BYTE nc;
                UINT di, ni;
                WORD hash = xname_sum(fs.lfnbuf); /* Hash value of the name to find */
                while ((res = dir_read(dp, 0)) == FR_OK)
                { /* Read an item */
                    if (ld_word(fs.dirbuf.AsSpan(36)) != hash) continue; /* Skip comparison if hash mismatched */
                    for (nc = fs.dirbuf[35], di = 32 * 2, ni = 0; nc != 0; nc--, di += 2, ni++)
                    { /* Compare the name */
                        if ((di % 32) == 0) di += 2;
                        if (char.ToUpper((char)ld_word(fs.dirbuf.AsSpan((int)di))) != char.ToUpper((char)fs.lfnbuf[ni])) break;
                    }
                    if (nc == 0 && fs.lfnbuf[ni] == 0) break; /* Name matched? */
                }
                return res;
            }
            /* On the FAT/FAT32 volume */
            ord = sum = 0xFF; dp.blk_ofs = 0xFFFFFFFF; /* Reset LFN sequence */
            do
            {
                res = move_window(fs, dp.sect);
                if (res != FR_OK) break;
                c = dp.dir[0];
                if (c == 0) { res = FR_NO_FILE; break; } /* Reached end of directory table */
                dp.obj.attr = a = (BYTE)(dp.dir[11] & 0x3F);
                if (c == 0xE5 || ((a & 0x08) != 0 && a != 0x0F))
                { /* An entry without valid data */
                    ord = 0xFF; dp.blk_ofs = 0xFFFFFFFF; /* Reset LFN sequence */
                }
                else
                {
                    if (a == 0x0F)
                    { /* Is it an LFN entry? */
                        if ((dp.fn[11] & 0x40) == 0)
                        {
                            if ((c & 0x40) != 0)
                            { /* Is it start of an entry set? */
                                c &= unchecked((BYTE)~0x40);
                                ord = c; /* Number of LFN entries */
                                dp.blk_ofs = dp.dptr; /* Start offset of LFN */
                                sum = dp.dir[13]; /* Sum of the SFN */
                            }
                            /* Check validity of the LFN entry and compare it with given name */
                            ord = (BYTE)((c == ord && sum == dp.dir[13] && cmp_lfn(fs.lfnbuf, dp.dir) != 0) ? ord - 1 : 0xFF);
                        }
                    }
                    else
                    { /* SFN entry */
                        if (ord == 0 && sum == sum_sfn(dp.dir)) break; /* LFN matched? */
                        if ((dp.fn[11] & 0x01) == 0 && !memcmp(dp.dir, dp.fn, 11)) break; /* SFN matched? */
                        ord = 0xFF; dp.blk_ofs = 0xFFFFFFFF; /* Not matched, reset LFN sequence */
                    }
                }
                res = dir_next(dp, 0); /* Next entry */
            } while (res == FR_OK);
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Register an object to the directory                                   */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_register( /* FR_OK:succeeded, FR_DENIED:no free entry or too many SFN collision, FR_DISK_ERR:disk error */
         DIR dp /* Target directory with object name to be created */
        )
        {
            FRESULT res = 0;
            FATFS fs = dp.obj.fs;
            UINT n, len, n_ent;
            Span<BYTE> snSpan = stackalloc BYTE[12];
            Ptr<BYTE> sn = snSpan;
            BYTE sum;
            if ((dp.fn[11] & (0x20 | 0x80)) != 0) return FR_INVALID_NAME; /* Check name validity */
            for (len = 0; fs.lfnbuf[len] != 0; len++) ; /* Get lfn length */
            if (fs.fs_type == 4)
            { /* On the exFAT volume */
                n_ent = (len + 14) / 15 + 2; /* Number of entries to allocate (85+C0+C1s) */
                res = dir_alloc(dp, n_ent); /* Allocate directory entries */
                if (res != FR_OK) return res;
                dp.blk_ofs = dp.dptr - 32 * (n_ent - 1); /* Set the allocated entry block offset */
                if ((dp.obj.stat & 4) != 0)
                { /* Has the directory been stretched by new allocation? */
                    dp.obj.stat &= unchecked((BYTE)(~4));
                    res = fill_first_frag(ref dp.obj); /* Fill the first fragment on the FAT if needed */
                    if (res != FR_OK) return res;
                    res = fill_last_frag(ref dp.obj, dp.clust, 0xFFFFFFFF); /* Fill the last fragment on the FAT if needed */
                    if (res != FR_OK) return res;
                    if (dp.obj.sclust != 0)
                    { /* Is it a sub-directory? */
                        DIR dj = new();
                        res = load_obj_xdir(dj, ref dp.obj); /* Load the object status */
                        if (res != FR_OK) return res;
                        dp.obj.objsize += (DWORD)fs.csize * (fs.ssize); /* Increase the directory size by cluster size */
                        st_qword(fs.dirbuf.AsSpan(56), dp.obj.objsize);
                        st_qword(fs.dirbuf.AsSpan(40), dp.obj.objsize);
                        fs.dirbuf[33] = (BYTE)(dp.obj.stat | 1); /* Update the allocation status */
                        res = store_xdir(dj); /* Store the object status */
                        if (res != FR_OK) return res;
                    }
                }
                create_xdir(fs.dirbuf, fs.lfnbuf); /* Create on-memory directory block to be written later */
                return FR_OK;
            }
            /* On the FAT/FAT32 volume */
            memcpy(sn, dp.fn, 12);
            if ((sn[11] & 0x01) != 0)
            { /* When LFN is out of 8.3 format, generate a numbered name */
                dp.fn[11] = 0x40; /* Find only SFN */
                for (n = 1; n < 100; n++)
                {
                    gen_numname(dp.fn, sn, fs.lfnbuf, n); /* Generate a numbered name */
                    res = dir_find(dp); /* Check if the name collides with existing SFN */
                    if (res != FR_OK) break;
                }
                if (n == 100) return FR_DENIED; /* Abort if too many collisions */
                if (res != FR_NO_FILE) return res; /* Abort if the result is other than 'not collided' */
                dp.fn[11] = sn[11];
            }
            /* Create an SFN with/without LFNs. */
            n_ent = (sn[11] & 0x02) != 0 ? (len + 12) / 13 + 1 : 1; /* Number of entries to allocate */
            res = dir_alloc(dp, n_ent); /* Allocate entries */
            if (res == FR_OK && (--n_ent) != 0)
            { /* Set LFN entry if needed */
                res = dir_sdi(dp, dp.dptr - n_ent * 32);
                if (res == FR_OK)
                {
                    sum = sum_sfn(dp.fn); /* Checksum value of the SFN tied to the LFN */
                    do
                    { /* Store LFN entries in bottom first */
                        res = move_window(fs, dp.sect);
                        if (res != FR_OK) break;
                        put_lfn(fs.lfnbuf, dp.dir, (BYTE)n_ent, sum);
                        fs.wflag = 1;
                        res = dir_next(dp, 0); /* Next entry */
                    } while (res == FR_OK && (--n_ent) != 0);
                }
            }
            /* Set SFN entry */
            if (res == FR_OK)
            {
                res = move_window(fs, dp.sect);
                if (res == FR_OK)
                {
                    memset(dp.dir, 0, 32); /* Clean the entry */
                    memcpy(dp.dir + 0, dp.fn, 11); /* Put SFN */
                    dp.dir[12] = (BYTE)(dp.fn[11] & (0x08 | 0x10)); /* Put NT flag */
                    fs.wflag = 1;
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Remove an object from the directory                                   */
        /*-----------------------------------------------------------------------*/
        public FRESULT dir_remove( /* FR_OK:Succeeded, FR_DISK_ERR:A disk error */
         DIR dp /* Directory object pointing the entry to be removed */
        )
        {
            FRESULT res;
            FATFS fs = dp.obj.fs;
            DWORD last = dp.dptr;
            res = (dp.blk_ofs == 0xFFFFFFFF) ? FR_OK : dir_sdi(dp, dp.blk_ofs); /* Goto top of the entry block if LFN is exist */
            if (res == FR_OK)
            {
                do
                {
                    res = move_window(fs, dp.sect);
                    if (res != FR_OK) break;
                    if (true && fs.fs_type == 4)
                    { /* On the exFAT volume */
                        dp.dir[0] &= 0x7F; /* Clear the entry InUse flag. */
                    }
                    else
                    { /* On the FAT/FAT32 volume */
                        dp.dir[0] = 0xE5; /* Mark the entry 'deleted'. */
                    }
                    fs.wflag = 1;
                    if (dp.dptr >= last) break; /* If reached last entry then all entries of the object has been deleted. */
                    res = dir_next(dp, 0); /* Next entry */
                } while (res == FR_OK);
                if (res == FR_NO_FILE) res = FR_INT_ERR;
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Get file information from directory entry                             */
        /*-----------------------------------------------------------------------*/
        public void get_fileinfo(
         DIR dp, /* Pointer to the directory object */
         FILINFO fno /* Pointer to the file information to be filled */
        )
        {
            UINT si, di;
            BYTE lcf;
            WCHAR wc, hs;
            FATFS fs = dp.obj.fs;
            UINT nw;
            fno.fname[0] = (WCHAR)0; /* Invaidate file info */
            if (dp.sect == 0) return; /* Exit if read pointer has reached end of directory */
            if (fs.fs_type == 4)
            { /* exFAT volume */
                UINT nc = 0;
                si = 32 * 2; di = 0; /* 1st C1 entry in the entry block */
                hs = (WCHAR)0;
                while (nc < fs.dirbuf[35])
                {
                    if (si >= ((255 + 44U) / 15 * 32))
                    { /* Truncated directory block? */
                        di = 0; break;
                    }
                    if ((si % 32) == 0) si += 2; /* Skip entry type field */
                    wc = (WCHAR)ld_word(fs.dirbuf.AsSpan((int)si)); si += 2; nc++; /* Get a character */
                    if (hs == 0 && ((wc) >= 0xD800 && (wc) <= 0xDFFF))
                    { /* Is it a surrogate? */
                        hs = wc; continue; /* Get low surrogate */
                    }
                    nw = put_utf((DWORD)hs << 16 | wc, fno.fname + di, 255 - di); /* Store it in API encoding */
                    if (nw == 0)
                    { /* Buffer overflow or wrong char? */
                        di = 0; break;
                    }
                    di += nw;
                    hs = (WCHAR)0;
                }
                if (hs != 0) di = 0; /* Broken surrogate pair? */
                if (di == 0) fno.fname[di++] = '?'; /* Inaccessible object name? */
                fno.fname[di] = (WCHAR)0; /* Terminate the name */
                fno.altname[0] = (WCHAR)0; /* exFAT does not support SFN */
                fno.fattrib = (BYTE)(fs.dirbuf[4] & 0x37); /* Attribute */
                fno.fsize = (fno.fattrib & 0x10) != 0 ? 0 : ld_qword(fs.dirbuf.AsSpan(56)); /* Size */
                fno.ftime = ld_word(fs.dirbuf.AsSpan(12 + 0)); /* Time */
                fno.fdate = ld_word(fs.dirbuf.AsSpan(12 + 2)); /* Date */
                return;
            }
            else
            { /* FAT/FAT32 volume */
                if (dp.blk_ofs != 0xFFFFFFFF)
                { /* Get LFN if available */
                    si = di = 0;
                    hs = (WCHAR)0;
                    while (fs.lfnbuf[si] != 0)
                    {
                        wc = fs.lfnbuf[si++]; /* Get an LFN character (UTF-16) */
                        if (hs == 0 && ((wc) >= 0xD800 && (wc) <= 0xDFFF))
                        { /* Is it a surrogate? */
                            hs = wc; continue; /* Get low surrogate */
                        }
                        nw = put_utf((DWORD)hs << 16 | wc, fno.fname + di, 255 - di); /* Store it in API encoding */
                        if (nw == 0)
                        { /* Buffer overflow or wrong char? */
                            di = 0; break;
                        }
                        di += nw;
                        hs = (WCHAR)0;
                    }
                    if (hs != 0) di = 0; /* Broken surrogate pair? */
                    fno.fname[di] = (WCHAR)0; /* Terminate the LFN (null string means LFN is invalid) */
                }
            }
            si = di = 0;
            while (si < 11)
            { /* Get SFN from SFN entry */
                wc = (char)dp.dir[si++]; /* Get a char */
                if (wc == ' ') continue; /* Skip padding spaces */
                if (wc == 0x05) wc = (WCHAR)0xE5; /* Restore replaced DDEM character */
                if (si == 9 && di < 12) fno.altname[di++] = '.'; /* Insert a . if extension is exist */
                if (dbc_1st((BYTE)wc) != 0 && si != 8 && si != 11 && dbc_2nd(dp.dir[si]) != 0)
                { /* Make a DBC if needed */
                    wc = (WCHAR)(wc << 8 | dp.dir[si++]);
                }
                wc = ff_oem2uni(wc); /* ANSI/OEM -> Unicode */
                if (wc == 0)
                { /* Wrong char in the current code page? */
                    di = 0; break;
                }
                nw = put_utf(wc, fno.altname + di, 12 - di); /* Store it in API encoding */
                if (nw == 0)
                { /* Buffer overflow? */
                    di = 0; break;
                }
                di += nw;
            }
            fno.altname[di] = (WCHAR)0; /* Terminate the SFN  (null string means SFN is invalid) */
            if (fno.fname[0] == 0)
            { /* If LFN is invalid, altname[] needs to be copied to fname[] */
                if (di == 0)
                { /* If LFN and SFN both are invalid, this object is inaccessible */
                    fno.fname[di++] = (WCHAR)'?';
                }
                else
                {
                    for (si = di = 0, lcf = 0x08; fno.altname[si] != 0; si++, di++)
                    { /* Copy altname[] to fname[] with case information */
                        wc = (WCHAR)fno.altname[si];
                        if (wc == '.') lcf = 0x10;
                        if (((wc) >= 'A' && (wc) <= 'Z') && (dp.dir[12] & lcf) != 0) wc += (WCHAR)0x20;
                        fno.fname[di] = (TCHAR)wc;
                    }
                }
                fno.fname[di] = (WCHAR)0; /* Terminate the LFN */
                if (dp.dir[12] == 0) fno.altname[0] = (WCHAR)0; /* Altname is not needed if neither LFN nor case info is exist. */
            }
            fno.fattrib = (BYTE)(dp.dir[11] & 0x3F); /* Attribute */
            fno.fsize = ld_dword(dp.dir + 28); /* Size */
            fno.ftime = ld_word(dp.dir + 22 + 0); /* Time */
            fno.fdate = ld_word(dp.dir + 22 + 2); /* Date */
        }
        /*-----------------------------------------------------------------------*/
        /* Pick a top segment and create the object name in directory form       */
        /*-----------------------------------------------------------------------*/
        public FRESULT create_name( /* FR_OK: successful, FR_INVALID_NAME: could not create */
         DIR dp, /* Pointer to the directory object */
        ref ReadOnlyPtr<TCHAR> path /* Pointer to pointer to the segment in the path string */
        )
        {
            BYTE b, cf;
            WCHAR wc;
            Ptr<WCHAR> lfn;
            ReadOnlyPtr<TCHAR> p;
            DWORD uc;
            UINT i, ni, si, di;
            /* Create LFN into LFN working buffer */
            p = path; lfn = dp.obj.fs.lfnbuf; di = 0;
            for (; ; )
            {
                uc = tchar2uni(ref p); /* Get a character */
                if (uc == 0xFFFFFFFF) return FR_INVALID_NAME; /* Invalid code or UTF decode error */
                if (uc >= 0x10000) lfn[di++] = (WCHAR)(uc >> 16); /* Store high surrogate if needed */
                wc = (WCHAR)uc;
                if (wc < ' ' || ((wc) == '/' || (wc) == '\\')) break; /* Break if end of the path or a separator is found */
                if (wc < 0x80 && strchr("*:<>|\"?\x7F", (WCHAR)wc)) return FR_INVALID_NAME; /* Reject illegal characters for LFN */
                if (di >= 255) return FR_INVALID_NAME; /* Reject too long name */
                lfn[di++] = wc; /* Store the Unicode character */
                if (p.IsEmpty)
                    break;
            }
            if (wc < ' ')
            { /* Stopped at end of the path? */
                cf = 0x04; /* Last segment */
            }
            else
            { /* Stopped at a separator */
                while (!p.IsEmpty && ((p.Value) == '/' || (p.Value) == '\\')) p++; /* Skip duplicated separators if exist */
                cf = 0; /* Next segment may follow */
                if (p.IsEmpty || ((UINT)(p.Value) < (3 != 0 ? ' ' : '!'))) cf = 0x04; /* Ignore terminating separator */
            }
            path = p; /* Return pointer to the next segment */
            while (di != 0)
            { /* Snip off trailing spaces and dots if exist */
                wc = lfn[di - 1];
                if (wc != ' ' && wc != '.') break;
                di--;
            }
            lfn[di] = (WCHAR)0; /* LFN is created into the working buffer */
            if (di == 0) return FR_INVALID_NAME; /* Reject null name */
            /* Create SFN in directory form */
            for (si = 0; lfn[si] == ' '; si++) ; /* Remove leading spaces */
            if (si > 0 || lfn[si] == '.') cf |= 0x01 | 0x02; /* Is there any leading space or dot? */
            while (di > 0 && lfn[di - 1] != '.') di--; /* Find last dot (di<=si: no extension) */
            memset(dp.fn, (byte)' ', 11);
            i = b = 0; ni = 8;
            for (; ; )
            {
                wc = lfn[si++]; /* Get an LFN character */
                if (wc == 0) break; /* Break on end of the LFN */
                if (wc == ' ' || (wc == '.' && si != di))
                { /* Remove embedded spaces and dots */
                    cf |= 0x01 | 0x02;
                    continue;
                }
                if (i >= ni || si == di)
                { /* End of field? */
                    if (ni == 11)
                    { /* Name extension overflow? */
                        cf |= 0x01 | 0x02;
                        break;
                    }
                    if (si != di) cf |= 0x01 | 0x02; /* Name body overflow? */
                    if (si > di) break; /* No name extension? */
                    si = di; i = 8; ni = 11; b <<= 2; /* Enter name extension */
                    continue;
                }
                if (wc >= 0x80)
                { /* Is this an extended character? */
                    cf |= 0x02; /* LFN entry needs to be created */
                    wc = ff_uni2oem(wc); /* Unicode ==> ANSI/OEM code */
                    if ((wc & 0x80) != 0) wc = (WCHAR)ExCvt[wc & 0x7F]; /* Convert extended character to upper (SBCS) */
                }
                if (wc >= 0x100)
                { /* Is this a DBC? */
                    if (i >= ni - 1)
                    { /* Field overflow? */
                        cf |= 0x01 | 0x02;
                        i = ni; continue; /* Next field */
                    }
                    dp.fn[i++] = (BYTE)(wc >> 8); /* Put 1st byte */
                }
                else
                { /* SBC */
                    if (wc == 0 || strchr("+,;=[]", (char)wc))
                    { /* Replace illegal characters for SFN */
                        wc = '_'; cf |= 0x01 | 0x02;/* Lossy conversion */
                    }
                    else
                    {
                        if (((wc) >= 'A' && (wc) <= 'Z'))
                        { /* ASCII upper case? */
                            b |= 2;
                        }
                        if (((wc) >= 'a' && (wc) <= 'z'))
                        { /* ASCII lower case? */
                            b |= 1; wc -= (WCHAR)0x20;
                        }
                    }
                }
                dp.fn[i++] = (BYTE)wc;
            }
            if (dp.fn[0] == 0xE5) dp.fn[0] = 0x05; /* If the first character collides with DDEM, replace it with RDDEM */
            if (ni == 8) b <<= 2; /* Shift capital flags if no extension */
            if ((b & 0x0C) == 0x0C || (b & 0x03) == 0x03) cf |= 0x02; /* LFN entry needs to be created if composite capitals */
            if ((cf & 0x02) == 0)
            { /* When LFN is in 8.3 format without extended character, NT flags are created */
                if ((b & 0x01) != 0) cf |= 0x10; /* NT flag (Extension has small capital letters only) */
                if ((b & 0x04) != 0) cf |= 0x08; /* NT flag (Body has small capital letters only) */
            }
            dp.fn[11] = cf; /* SFN is created into dp.fn[] */
            return FR_OK;
        }
        /*-----------------------------------------------------------------------*/
        /* Follow a file path                                                    */
        /*-----------------------------------------------------------------------*/
        public FRESULT follow_path( /* FR_OK(0): successful, !=0: error code */
         DIR dp, /* Directory object to return last directory and found object */
        ReadOnlyPtr<WCHAR> path /* Full-path string to find a file or directory */
        )
        {
            FRESULT res;
            BYTE ns;
            FATFS fs = dp.obj.fs;
            { /* With heading separator */
                while (((path.Value) == '/' || (path.Value) == '\\')) path++; /* Strip separators */
                dp.obj.sclust = 0; /* Start from the root directory */
            }
            dp.obj.n_frag = 0; /* Invalidate last fragment counter of the object */
            if ((UINT)path.Value < ' ')
            { /* Null path name is the origin directory itself */
                dp.fn[11] = 0x80;
                res = dir_sdi(dp, 0);
            }
            else
            { /* Follow path */
                for (; ; )
                {
                    res = create_name(dp, ref path); /* Get a segment name of the path */
                    if (res != FR_OK) break;
                    res = dir_find(dp); /* Find an object with the segment name */
                    ns = dp.fn[11];
                    if (res != FR_OK)
                    { /* Failed to find the object */
                        if (res == FR_NO_FILE)
                        { /* Object is not found */
                            if (false && (ns & 0x20) != 0)
                            { /* If dot entry is not exist, stay there */
                                if ((ns & 0x04) == 0) continue; /* Continue to follow if not last segment */
                                dp.fn[11] = 0x80;
                                res = FR_OK;
                            }
                            else
                            { /* Could not find the object */
                                if ((ns & 0x04) == 0) res = FR_NO_PATH; /* Adjust error code if not last segment */
                            }
                        }
                        break;
                    }
                    if ((ns & 0x04) != 0) break; /* Last segment matched. Function completed. */
                    /* Get into the sub-directory */
                    if ((dp.obj.attr & 0x10) == 0)
                    { /* It is not a sub-directory and cannot follow */
                        res = FR_NO_PATH; break;
                    }
                    if (fs.fs_type == 4)
                    { /* Save containing directory information for next dir */
                        dp.obj.c_scl = dp.obj.sclust;
                        dp.obj.c_size = ((DWORD)dp.obj.objsize & 0xFFFFFF00) | dp.obj.stat;
                        dp.obj.c_ofs = dp.blk_ofs;
                        init_alloc_info(fs, ref dp.obj); /* Open next directory */
                    }
                    else
                    {
                        dp.obj.sclust = ld_clust(fs, fs.win + dp.dptr % (fs.ssize)); /* Open next directory */
                    }
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Get logical drive number from path name                               */
        /*-----------------------------------------------------------------------*/
        public static int get_ldnumber( /* Returns logical drive number (-1:invalid drive number or null pointer) */
          ref ReadOnlyPtr<TCHAR> path /* Pointer to pointer to the path name */
        )
        {
            ReadOnlyPtr<TCHAR> tp;
            ReadOnlyPtr<TCHAR> tt;
            TCHAR chr;
            int i;
            tt = tp = path;
            if (tp.IsEmpty) return -1; /* Invalid path name? */
            do
            { /* Find a colon in the path */
                chr = (tt++).Value;
            } while (!((UINT)(chr) < (false ? ' ' : '!')) && chr != ':');
            if (chr == ':')
            { /* Is there a DOS/Windows style volume ID? */
                i = 10;
                if ((tp.Value >= '0' && tp.Value <= '9') && tp + 2 == tt)
                { /* Is it a numeric volume ID + colon? */
                    i = (int)tp.Value - '0'; /* Get the logical drive number */
                }
                if (i >= 10) return -1; /* Not found or invalid volume ID */
                path = tt; /* Snip the drive prefix off */
                return i; /* Return the found drive number */
            }
            /* No drive prefix */
            return 0; /* Default drive is 0 */
        }
        /*-----------------------------------------------------------------------*/
        /* GPT support functions                                                 */
        /*-----------------------------------------------------------------------*/
        /* Calculate CRC32 in byte-by-byte */
        public static DWORD crc32( /* Returns next CRC value */
         DWORD crc, /* Current CRC value */
         BYTE d /* A byte to be processed */
        )
        {
            BYTE b;
            for (b = 1; b != 0; b <<= 1)
            {
                crc ^= (d & b) != 0 ? (BYTE)1 : (BYTE)0;
                crc = (crc & 1) != 0 ? crc >> 1 ^ 0xEDB88320 : crc >> 1;
            }
            return crc;
        }
        /* Check validity of GPT header */
        public static int test_gpt_header( /* 0:Invalid, 1:Valid */
         Ptr<BYTE> gpth /* Pointer to the GPT header */
        )
        {
            UINT i;
            DWORD bcc, hlen;
            if (memcmp(gpth + 0, "EFI PART\0\0\x01"u8, 12)) return 0; /* Check signature and version (1.0) */
            hlen = ld_dword(gpth + 12); /* Check header size */
            if (hlen < 92 || hlen > 512) return 0;
            for (i = 0, bcc = 0xFFFFFFFF; i < hlen; i++)
            { /* Check header BCC */
                bcc = crc32(bcc, i - 16 < 4 ? (BYTE)0 : gpth[i]);
            }
            if (~bcc != ld_dword(gpth + 16)) return 0;
            if (ld_dword(gpth + 84) != 128) return 0; /* Table entry size (must be SZ_GPTE bytes) */
            if (ld_dword(gpth + 80) > 128) return 0; /* Table size (must be 128 entries or less) */
            return 1;
        }
        /* Generate a random value */
        public static DWORD make_rand( /* Returns a seed value for next */
         DWORD seed, /* Seed value */
         Ptr<BYTE> buff, /* Output buffer */
         UINT n /* Data length */
        )
        {
            UINT r;
            if (seed == 0) seed = 1;
            do
            {
                for (r = 0; r < 8; r++) seed = (seed & 1) != 0 ? seed >> 1 ^ 0xA3000000 : seed >> 1; /* Shift 8 bits the 32-bit LFSR */
                (buff++).Value = (BYTE)seed;
            } while ((--n) != 0);
            return seed;
        }
        /*-----------------------------------------------------------------------*/
        /* Load a sector and check if it is an FAT VBR                           */
        /*-----------------------------------------------------------------------*/
        /* Check what the sector is */
        UINT check_fs( /* 0:FAT/FAT32 VBR, 1:exFAT VBR, 2:Not FAT and valid BS, 3:Not FAT and invalid BS, 4:Disk error */
         FATFS fs, /* Filesystem object */
         LBA_t sect /* Sector to load and check if it is an FAT-VBR or not */
        )
        {
            WORD w, sign;
            BYTE b;
            fs.wflag = 0; fs.winsect = unchecked((LBA_t)0 - 1); /* Invaidate window */
            if (move_window(fs, sect) != FR_OK) return 4; /* Load the boot sector */
            sign = ld_word(fs.win + 510);
            if (sign == 0xAA55 && !memcmp(fs.win + 0, [0xEB, 0x76, 0x90, .. " EXFAT   "u8], 11)) return 1; /* It is an exFAT VBR */
            b = fs.win[0];
            if (b == 0xEB || b == 0xE9 || b == 0xE8)
            { /* Valid JumpBoot code? (short jump, near jump or near call) */
                if (sign == 0xAA55 && !memcmp(fs.win + 82, "FAT32   "u8, 8))
                {
                    return 0; /* It is an FAT32 VBR */
                }
                /* FAT volumes created in the early MS-DOS era lack BS_55AA and BS_FilSysType, so FAT VBR needs to be identified without them. */
                w = ld_word(fs.win + 11);
                b = fs.win[13];
                if ((w & (w - 1)) == 0 && w >= 512 && w <= 4096 /* Properness of sector size (512-4096 and 2^n) */
                 && b != 0 && (b & (b - 1)) == 0 /* Properness of cluster size (2^n) */
        && ld_word(fs.win + 14) != 0 /* Properness of number of reserved sectors (MNBZ) */
        && (UINT)fs.win[16] - 1 <= 1 /* Properness of number of FATs (1 or 2) */
        && ld_word(fs.win + 17) != 0 /* Properness of root dir size (MNBZ) */
        && (ld_word(fs.win + 19) >= 128 || ld_dword(fs.win + 32) >= 0x10000) /* Properness of volume size (>=128) */
        && ld_word(fs.win + 22) != 0)
                { /* Properness of FAT size (MNBZ) */
                    return 0; /* It can be presumed an FAT VBR */
                }
            }
            return sign == 0xAA55 ? 2U : 3U; /* Not an FAT VBR (with valid or invalid BS) */
        }
        /* Find an FAT volume */
        /* (It supports only generic partitioning rules, MBR, GPT and SFD) */
        public UINT find_volume( /* Returns BS status found in the hosting drive */
         FATFS fs, /* Filesystem object */
                 UINT part /* Partition to fined = 0:find as SFD and partitions, >0:forced partition number */
                )
        {
            UINT fmt, i;
            Span<DWORD> mbp_pt_span = stackalloc DWORD[4];
            Ptr<DWORD> mbr_pt = mbp_pt_span;
            fmt = check_fs(fs, 0); /* Load sector 0 and check if it is an FAT VBR as SFD format */
            if (fmt != 2 && (fmt >= 3 || part == 0)) return fmt; /* Returns if it is an FAT VBR as auto scan, not a BS or disk error */
            /* Sector 0 is not an FAT VBR or forced partition number wants a partition */
            if (fs.win[446 + 4] == 0xEE)
            { /* GPT protective MBR? */
                DWORD n_ent, v_ent, ofs;
                QWORD pt_lba;
                if (move_window(fs, 1) != FR_OK) return 4; /* Load GPT header sector (next to MBR) */
                if (test_gpt_header(fs.win) == 0) return 3; /* Check if GPT header is valid */
                n_ent = ld_dword(fs.win + 80); /* Number of entries */
                pt_lba = ld_qword(fs.win + 72); /* Table location */
                for (v_ent = i = 0; i < n_ent; i++)
                { /* Find FAT partition */
                    if (move_window(fs, pt_lba + i * 128 / (fs.ssize)) != FR_OK) return 4; /* PT sector */
                    ofs = i * 128 % (fs.ssize); /* Offset in the sector */
                    if (!memcmp(fs.win + ofs + 0, GUID_MS_Basic, 16))
                    { /* MS basic data partition? */
                        v_ent++;
                        fmt = check_fs(fs, ld_qword(fs.win + ofs + 32)); /* Load VBR and check status */
                        if (part == 0 && fmt <= 1) return fmt; /* Auto search (valid FAT volume found first) */
                        if (part != 0 && v_ent == part) return fmt; /* Forced partition order (regardless of it is valid or not) */
                    }
                }
                return 3; /* Not found */
            }
            if (true && part > 4) return 3; /* MBR has 4 partitions max */
            for (i = 0; i < 4; i++)
            { /* Load partition offset in the MBR */
                mbr_pt[i] = ld_dword(fs.win + 446 + i * 16 + 8);
            }
            i = part != 0 ? part - 1 : 0; /* Table index to find first */
            do
            { /* Find an FAT volume */
                fmt = mbr_pt[i] != 0 ? check_fs(fs, mbr_pt[i]) : 3; /* Check if the partition is FAT */
            } while (part == 0 && fmt >= 2 && ++i < 4);
            return fmt;
        }
        /*-----------------------------------------------------------------------*/
        /* Determine logical drive number and mount the volume if needed         */
        /*-----------------------------------------------------------------------*/
        public FRESULT mount_volume( /* FR_OK(0): successful, !=0: an error occurred */
         ref ReadOnlyPtr<WCHAR> path, /* Pointer to pointer to the path name (drive number) */
         out FATFS rfs, /* Pointer to pointer to the found filesystem object */
         BYTE mode /* Desiered access mode to check write protection */
        )
        {
            int vol;
            FATFS fs;
            DSTATUS stat;
            LBA_t bsect;
            DWORD tsect, sysect, fasize, nclst, szbfat;
            WORD nrsv;
            UINT fmt;
            /* Get logical drive number */
            rfs = null!;
            vol = get_ldnumber(ref path);
            if (vol < 0) return FR_INVALID_DRIVE;
            if (vol >= FatFs.Length)
                Array.Resize(ref FatFs, vol + 1);
            /* Check if the filesystem object is valid or not */
            fs = FatFs[vol]!; /* Get pointer to the filesystem object */
            if (fs == null) return FR_NOT_ENABLED; /* Is the filesystem object available? */
            rfs = fs; /* Return pointer to the filesystem object */
            mode &= unchecked((BYTE)~0x01); /* Desired access mode, write access or not */
            if (fs.fs_type != 0)
            { /* If the volume has been mounted */
                stat = (DSTATUS)disk_status(fs.pdrv);
                if ((stat & 0x01) == 0)
                { /* and the physical drive is kept initialized */
                    if (true && mode != 0 && (stat & 0x04) != 0)
                    { /* Check write protection if needed */
                        return FR_WRITE_PROTECTED;
                    }
                    return FR_OK; /* The filesystem object is already valid */
                }
            }
            /* The filesystem object is not valid. */
            /* Following code attempts to mount the volume. (find an FAT volume, analyze the BPB and initialize the filesystem object) */
            fs.fs_type = 0; /* Invalidate the filesystem object */
            stat = (DSTATUS)disk_initialize(fs.pdrv); /* Initialize the volume hosting physical drive */
            if ((stat & 0x01) != 0)
            { /* Check if the initialization succeeded */
                return FR_NOT_READY; /* Failed to initialize due to no medium or hard error */
            }
            if (true && mode != 0 && (stat & 0x04) != 0)
            { /* Check disk write protection if needed */
                return FR_WRITE_PROTECTED;
            }
            if (disk_ioctl(fs.pdrv, 2, MemoryMarshal.AsBytes(new Span<WORD>(ref fs.ssize))) != RES_OK) return FR_DISK_ERR;
            if ((fs.ssize) > 4096 || (fs.ssize) < 512 || ((fs.ssize) & ((fs.ssize) - 1)) != 0) return FR_DISK_ERR;
            /* Find an FAT volume on the hosting drive */
            fmt = find_volume(fs, VolToPart(vol).pt);
            if (fmt == 4) return FR_DISK_ERR; /* An error occurred in the disk I/O layer */
            if (fmt >= 2) return FR_NO_FILESYSTEM; /* No FAT volume is found */
            bsect = fs.winsect; /* Volume offset in the hosting physical drive */
            /* An FAT volume is found (bsect). Following code initializes the filesystem object */
            if (fmt == 1)
            {
                QWORD maxlba;
                DWORD so, cv, bcl, i;
                for (i = 11; i < 11 + 53 && fs.win[i] == 0; i++) ; /* Check zero filler */
                if (i < 11 + 53) return FR_NO_FILESYSTEM;
                if (ld_word(fs.win + 104) != 0x100) return FR_NO_FILESYSTEM; /* Check exFAT version (must be version 1.0) */
                if (1 << fs.win[108] != (fs.ssize))
                { /* (BPB_BytsPerSecEx must be equal to the physical sector size) */
                    return FR_NO_FILESYSTEM;
                }
                maxlba = ld_qword(fs.win + 72) + bsect; /* Last LBA of the volume + 1 */
                if (false && maxlba >= 0x100000000) return FR_NO_FILESYSTEM; /* (It cannot be accessed in 32-bit LBA) */
                fs.fsize = ld_dword(fs.win + 84); /* Number of sectors per FAT */
                fs.n_fats = fs.win[110]; /* Number of FATs */
                if (fs.n_fats != 1) return FR_NO_FILESYSTEM; /* (Supports only 1 FAT) */
                fs.csize = (WORD)(1 << fs.win[109]); /* Cluster size */
                if (fs.csize == 0) return FR_NO_FILESYSTEM; /* (Must be 1..32768 sectors) */
                nclst = ld_dword(fs.win + 92); /* Number of clusters */
                if (nclst > 0x7FFFFFFD) return FR_NO_FILESYSTEM; /* (Too many clusters) */
                fs.n_fatent = nclst + 2;
                /* Boundaries and Limits */
                fs.volbase = bsect;
                fs.database = bsect + ld_dword(fs.win + 88);
                fs.fatbase = bsect + ld_dword(fs.win + 80);
                if (maxlba < (QWORD)fs.database + nclst * fs.csize) return FR_NO_FILESYSTEM; /* (Volume size must not be smaller than the size required) */
                fs.dirbase = ld_dword(fs.win + 96);
                /* Get bitmap location and check if it is contiguous (implementation assumption) */
                so = i = 0;
                for (; ; )
                { /* Find the bitmap entry in the root directory (in only first cluster) */
                    if (i == 0)
                    {
                        if (so >= fs.csize) return FR_NO_FILESYSTEM; /* Not found? */
                        if (move_window(fs, clst2sect(fs, (DWORD)fs.dirbase) + so) != FR_OK) return FR_DISK_ERR;
                        so++;
                    }
                    if (fs.win[i] == 0x81) break; /* Is it a bitmap entry? */
                    i = (i + 32) % (fs.ssize); /* Next entry */
                }
                bcl = ld_dword(fs.win + i + 20); /* Bitmap cluster */
                if (bcl < 2 || bcl >= fs.n_fatent) return FR_NO_FILESYSTEM; /* (Wrong cluster#) */
                fs.bitbase = fs.database + fs.csize * (bcl - 2); /* Bitmap sector */
                for (; ; )
                { /* Check if bitmap is contiguous */
                    if (move_window(fs, fs.fatbase + bcl / (((QWORD)fs.ssize) / 4)) != FR_OK) return FR_DISK_ERR;
                    cv = ld_dword(fs.win + checked((UINT)(bcl % (((QWORD)fs.ssize) / 4) * 4)));
                    if (cv == 0xFFFFFFFF) break; /* Last link? */
                    if (cv != ++bcl) return FR_NO_FILESYSTEM; /* Fragmented bitmap? */
                }
                fs.last_clst = fs.free_clst = 0xFFFFFFFF; /* Invalidate cluster allocation information */
                fs.fsi_flag = 0; /* Enable to sync PercInUse value in VBR */
                fmt = 4; /* FAT sub-type */
            }
            else
            {
                if (ld_word(fs.win + 11) != (fs.ssize)) return FR_NO_FILESYSTEM; /* (BPB_BytsPerSec must be equal to the physical sector size) */
                fasize = ld_word(fs.win + 22); /* Number of sectors per FAT */
                if (fasize == 0) fasize = ld_dword(fs.win + 36);
                fs.fsize = fasize;
                fs.n_fats = fs.win[16]; /* Number of FATs */
                if (fs.n_fats != 1 && fs.n_fats != 2) return FR_NO_FILESYSTEM; /* (Must be 1 or 2) */
                fasize *= fs.n_fats; /* Number of sectors for FAT area */
                fs.csize = fs.win[13]; /* Cluster size */
                if (fs.csize == 0 || (fs.csize & (fs.csize - 1)) != 0) return FR_NO_FILESYSTEM; /* (Must be power of 2) */
                fs.n_rootdir = ld_word(fs.win + 17); /* Number of root directory entries */
                if ((fs.n_rootdir % ((fs.ssize) / 32)) != 0) return FR_NO_FILESYSTEM; /* (Must be sector aligned) */
                tsect = ld_word(fs.win + 19); /* Number of sectors on the volume */
                if (tsect == 0) tsect = ld_dword(fs.win + 32);
                nrsv = ld_word(fs.win + 14); /* Number of reserved sectors */
                if (nrsv == 0) return FR_NO_FILESYSTEM; /* (Must not be 0) */
                /* Determine the FAT sub type */
                sysect = (UINT)(nrsv + fasize + fs.n_rootdir / ((fs.ssize) / 32)); /* RSV + FAT + DIR */
                if (tsect < sysect) return FR_NO_FILESYSTEM; /* (Invalid volume size) */
                nclst = (tsect - sysect) / fs.csize; /* Number of clusters */
                if (nclst == 0) return FR_NO_FILESYSTEM; /* (Invalid volume size) */
                fmt = 0;
                if (nclst <= 0x0FFFFFF5) fmt = 3;
                if (nclst <= 0xFFF5) fmt = 2;
                if (nclst <= 0xFF5) fmt = 1;
                if (fmt == 0) return FR_NO_FILESYSTEM;
                /* Boundaries and Limits */
                fs.n_fatent = nclst + 2; /* Number of FAT entries */
                fs.volbase = bsect; /* Volume start sector */
                fs.fatbase = bsect + nrsv; /* FAT start sector */
                fs.database = bsect + sysect; /* Data start sector */
                if (fmt == 3)
                {
                    if (ld_word(fs.win + 42) != 0) return FR_NO_FILESYSTEM; /* (Must be FAT32 revision 0.0) */
                    if (fs.n_rootdir != 0) return FR_NO_FILESYSTEM; /* (BPB_RootEntCnt must be 0) */
                    fs.dirbase = ld_dword(fs.win + 44); /* Root directory start cluster */
                    szbfat = fs.n_fatent * 4; /* (Needed FAT size) */
                }
                else
                {
                    if (fs.n_rootdir == 0) return FR_NO_FILESYSTEM; /* (BPB_RootEntCnt must not be 0) */
                    fs.dirbase = fs.fatbase + fasize; /* Root directory start sector */
                    szbfat = (fmt == 2) ? /* (Needed FAT size) */
        fs.n_fatent * 2 : fs.n_fatent * 3 / 2 + (fs.n_fatent & 1);
                }
                if (fs.fsize < (szbfat + ((fs.ssize) - 1)) / (fs.ssize)) return FR_NO_FILESYSTEM; /* (BPB_FATSz must not be less than the size needed) */
                /* Get FSInfo if available */
                fs.last_clst = fs.free_clst = 0xFFFFFFFF; /* Invalidate cluster allocation information */
                fs.fsi_flag = 0x80; /* Disable FSInfo by default */
                if (fmt == 3
        && ld_word(fs.win + 48) == 1 /* FAT32: Enable FSInfo feature only if FSInfo sector is next to VBR */
                 && move_window(fs, bsect + 1) == FR_OK)
                {
                    fs.fsi_flag = 0;
                    if (ld_dword(fs.win + 0) == 0x41615252 /* Load FSInfo data if available */
                     && ld_dword(fs.win + 484) == 0x61417272
                     && ld_dword(fs.win + 498) == 0xAA550000)
                    {
                        fs.free_clst = ld_dword(fs.win + 488);
                        fs.last_clst = ld_dword(fs.win + 492);
                    }
                }
            }
            fs.fs_type = (BYTE)fmt;/* FAT sub-type (the filesystem object gets valid) */
            fs.id = ++Fsid; /* Volume mount ID */
            return FR_OK;
        }

        /*-----------------------------------------------------------------------*/
        /* Check if the file/directory object is valid or not                    */
        /*-----------------------------------------------------------------------*/
        public FRESULT validate( /* Returns FR_OK or FR_INVALID_OBJECT */
         ref FFOBJID obj, /* Pointer to the FFOBJID, the 1st member in the FIL/DIR structure, to check validity */
         out FATFS rfs /* Pointer to pointer to the owner filesystem object to return */
        )
        {
            FRESULT res = FR_INVALID_OBJECT;
            if (obj.fs.fs_type != 0 && obj.id == obj.fs.id)
            { /* Test if the object is valid */
                if (((DSTATUS)disk_status(obj.fs.pdrv) & 0x01) == 0)
                { /* Test if the hosting physical drive is kept initialized */
                    res = FR_OK;
                }
            }
            if (res == FRESULT.FR_OK)
                rfs = obj.fs;
            else
                rfs = null!;
            return res;
        }
        /*---------------------------------------------------------------------------



           Public Functions (FatFs API)



        ----------------------------------------------------------------------------*/
        /*-----------------------------------------------------------------------*/
        /* Mount/Unmount a Logical Drive                                         */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_mount(
         FATFS? fs, /* Pointer to the filesystem object to be registered (NULL:unmount)*/
         ReadOnlySpan<WCHAR> pathSpan, /* Logical drive number to be mounted/unmounted */
         BYTE opt /* Mount option: 0=Do not mount (delayed mount), 1=Mount immediately */
        )
        {
            FATFS cfs;
            int vol;
            FRESULT res;
            ReadOnlyPtr<WCHAR> path = pathSpan;
            ReadOnlyPtr<WCHAR> rp = path;
            /* Get volume ID (logical drive number) */
            vol = get_ldnumber(ref rp);
            if (vol < 0) return FR_INVALID_DRIVE;
            if (vol >= FatFs.Length)
                Array.Resize(ref FatFs, vol + 1);
            cfs = FatFs[vol]!; /* Pointer to the filesystem object of the volume */
            if (cfs != null)
            { /* Unregister current filesystem object if registered */
                FatFs[vol] = null;
                cfs.fs_type = 0; /* Invalidate the filesystem object to be unregistered */
            }
            if (fs != null)
            { /* Register new filesystem object */
                fs.pdrv = (BYTE)(vol); /* Volume hosting physical drive */
                fs.fs_type = 0; /* Invalidate the new filesystem object */
                FatFs[vol] = fs; /* Register new fs object */
            }
            if (opt == 0) return FR_OK; /* Do not mount now, it will be mounted in subsequent file functions */
            res = mount_volume(ref path, out _, 0); /* Force mounted the volume */
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Open or Create a File                                                 */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_open(
         out FIL fp, /* Pointer to the blank file object */
         ReadOnlySpan<TCHAR> pathSpan, /* Pointer to the file name */
         MODE mode /* Access mode and open mode flags */
        )
        {
            ReadOnlyPtr<TCHAR> path = pathSpan;
            FRESULT res;
            DIR dj = new();
            FATFS fs;
            DWORD cl, bcs, clst, tm;
            LBA_t sc;
            FSIZE_t ofs;
            fp = new();
            /* Get logical drive number */
            mode &= (MODE)(false ? 0x01 : 0x01 | 0x02 | 0x08 | 0x04 | 0x10 | 0x30);
            res = mount_volume(ref path, out fs, (BYTE)mode);
            if (res == FR_OK)
            {
                dj.obj.fs = fs;
                fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(dj, path); /* Follow the file path */
                if (res == FR_OK)
                {
                    if ((dj.fn[11] & 0x80) != 0)
                    { /* Origin directory itself? */
                        res = FR_INVALID_NAME;
                    }
                }
                /* Create or Open a file */
                if (((BYTE)mode & (0x08 | 0x10 | 0x04)) != 0)
                {
                    if (res != FR_OK)
                    { /* No file, create new */
                        if (res == FR_NO_FILE)
                        { /* There is no file to open, create a new entry */
                            res = dir_register(dj);
                        }
                        mode |= (MODE)0x08; /* File is created */
                    }
                    else
                    { /* Any object with the same name is already existing */
                        if ((dj.obj.attr & (0x01 | 0x10)) != 0)
                        { /* Cannot overwrite it (R/O or DIR) */
                            res = FR_DENIED;
                        }
                        else
                        {
                            if (((BYTE)mode & 0x04) != 0) res = FR_EXIST; /* Cannot create as new file */
                        }
                    }
                    if (res == FR_OK && ((BYTE)mode & 0x08) != 0)
                    { /* Truncate the file if overwrite mode */
                        if (fs.fs_type == 4)
                        {
                            /* Get current allocation info */
                            fp.obj.fs = fs;
                            init_alloc_info(fs, ref fp.obj);
                            /* Set directory entry block initial state */
                            memset(fs.dirbuf.AsSpan(2), 0, 30); /* Clear 85 entry except for NumSec */
                            memset(fs.dirbuf.AsSpan(38), 0, 26); /* Clear C0 entry except for NumName and NameHash */
                            fs.dirbuf[4] = 0x20;
                            st_dword(fs.dirbuf.AsSpan(8), get_fattime());
                            fs.dirbuf[33] = 1;
                            res = store_xdir(dj);
                            if (res == FR_OK && fp.obj.sclust != 0)
                            { /* Remove the cluster chain if exist */
                                res = remove_chain(ref fp.obj, fp.obj.sclust, 0);
                                fs.last_clst = fp.obj.sclust - 1; /* Reuse the cluster hole */
                            }
                        }
                        else
                        {
                            /* Set directory entry initial state */
                            tm = get_fattime(); /* Set created time */
                            st_dword(dj.dir + 14, tm);
                            st_dword(dj.dir + 22, tm);
                            cl = ld_clust(fs, dj.dir); /* Get current cluster chain */
                            dj.dir[11] = 0x20; /* Reset attribute */
                            st_clust(fs, dj.dir, 0); /* Reset file allocation info */
                            st_dword(dj.dir + 28, 0);
                            fs.wflag = 1;
                            if (cl != 0)
                            { /* Remove the cluster chain if exist */
                                sc = fs.winsect;
                                res = remove_chain(ref dj.obj, cl, 0);
                                if (res == FR_OK)
                                {
                                    res = move_window(fs, sc);
                                    fs.last_clst = cl - 1; /* Reuse the cluster hole */
                                }
                            }
                        }
                    }
                }
                else
                { /* Open an existing file */
                    if (res == FR_OK)
                    { /* Is the object exsiting? */
                        if ((dj.obj.attr & 0x10) != 0)
                        { /* File open against a directory */
                            res = FR_NO_FILE;
                        }
                        else
                        {
                            if (((BYTE)mode & 0x02) != 0 && (dj.obj.attr & 0x01) != 0)
                            { /* Write mode open against R/O file */
                                res = FR_DENIED;
                            }
                        }
                    }
                }
                if (res == FR_OK)
                {
                    if (((BYTE)mode & 0x08) != 0) mode |= (MODE)0x40; /* Set file change flag if created or overwritten */
                    fp.dir_sect = fs.winsect; /* Pointer to the directory entry */
                    fp.dir_ofs = dj.dir_ofs;
                }
                if (res == FR_OK)
                {
                    if (fs.fs_type == 4)
                    {
                        fp.obj.c_scl = dj.obj.sclust; /* Get containing directory info */
                        fp.obj.c_size = ((DWORD)dj.obj.objsize & 0xFFFFFF00) | dj.obj.stat;
                        fp.obj.c_ofs = dj.blk_ofs;
                        init_alloc_info(fs, ref fp.obj);
                    }
                    else
                    {
                        fp.obj.sclust = ld_clust(fs, dj.dir); /* Get object allocation info */
                        fp.obj.objsize = ld_dword(dj.dir + 28);
                    }
                    fp.obj.fs = fs; /* Validate the file object */
                    fp.obj.id = fs.id;
                    fp.flag = (BYTE)mode; /* Set file access mode */
                    fp.err = 0; /* Clear error flag */
                    fp.sect = 0; /* Invalidate current data sector */
                    fp.fptr = 0; /* Set file pointer top of the file */
                    memset(fp.buf, 0, FIL.buf_size); /* Clear sector buffer */
                    if (((BYTE)mode & 0x20) != 0 && fp.obj.objsize > 0)
                    { /* Seek to end of file if FA_OPEN_APPEND is specified */
                        fp.fptr = fp.obj.objsize; /* Offset to seek */
                        bcs = (DWORD)fs.csize * (fs.ssize); /* Cluster size in byte */
                        clst = fp.obj.sclust; /* Follow the cluster chain */
                        for (ofs = fp.obj.objsize; res == FR_OK && ofs > bcs; ofs -= bcs)
                        {
                            clst = get_fat(ref fp.obj, clst);
                            if (clst <= 1) res = FR_INT_ERR;
                            if (clst == 0xFFFFFFFF) res = FR_DISK_ERR;
                        }
                        fp.clust = clst;
                        if (res == FR_OK && (ofs % (fs.ssize)) != 0)
                        { /* Fill sector buffer if not on the sector boundary */
                            sc = clst2sect(fs, clst);
                            if (sc == 0)
                            {
                                res = FR_INT_ERR;
                            }
                            else
                            {
                                fp.sect = sc + (DWORD)(ofs / (fs.ssize));
                                if (disk_read(fs.pdrv, fp.buf, fp.sect, 1) != RES_OK) res = FR_DISK_ERR;
                            }
                        }
                    }
                }
                ArrayPool<WCHAR>.Shared.Return(fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(fs.dirbuf);
                fs.lfnbuf = null!;
                fs.dirbuf = null!;
            }
            if (res != FR_OK && fp != null) fp.obj.fs = null!; /* Invalidate file object on error */
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Read File                                                             */
        /*-----------------------------------------------------------------------*/
        FRESULT f_read(
         FIL fp, /* Open file to be read */
         Ptr<byte> buff, /* Data buffer to store the read data */
         UINT btr, /* Number of bytes to read */
         out UINT br /* Number of bytes read */
        )
        {
            FRESULT res;
            FATFS fs;
            DWORD clst;
            LBA_t sect;
            FSIZE_t remain;
            UINT rcnt, cc, csect;
            Ptr<BYTE> rbuff = buff;
            br = 0; /* Clear read byte counter */
            res = validate(ref fp.obj, out fs); /* Check validity of the file object */
            if (res != FR_OK || (res = (FRESULT)fp.err) != FR_OK) return res; /* Check validity */
            if ((fp.flag & 0x01) == 0) return FR_DENIED; /* Check access mode */
            remain = fp.obj.objsize - fp.fptr;
            if (btr > remain) btr = (UINT)remain; /* Truncate btr by remaining bytes */
            for (; btr > 0; btr -= rcnt, br += rcnt, rbuff += rcnt, fp.fptr += rcnt)
            { /* Repeat until btr bytes read */
                if (fp.fptr % (fs.ssize) == 0)
                { /* On the sector boundary? */
                    csect = (UINT)(fp.fptr / ((UINT)fs.ssize) & ((UINT)fs.csize - 1)); /* Sector offset in the cluster */
                    if (csect == 0)
                    { /* On the cluster boundary? */
                        if (fp.fptr == 0)
                        { /* On the top of the file? */
                            clst = fp.obj.sclust; /* Follow cluster chain from the origin */
                        }
                        else
                        { /* Middle or end of the file */
                            {
                                clst = get_fat(ref fp.obj, fp.clust); /* Follow cluster chain on the FAT */
                            }
                        }
                        if (clst < 2) { fp.err = (BYTE)(FR_INT_ERR); return FR_INT_ERR; };
                        if (clst == 0xFFFFFFFF) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                        fp.clust = clst; /* Update current cluster */
                    }
                    sect = clst2sect(fs, fp.clust); /* Get current sector */
                    if (sect == 0) { fp.err = (BYTE)(FR_INT_ERR); return FR_INT_ERR; };
                    sect += csect;
                    cc = btr / (fs.ssize); /* When remaining bytes >= sector size, */
                    if (cc > 0)
                    { /* Read maximum contiguous sectors directly */
                        if (csect + cc > fs.csize)
                        { /* Clip at cluster boundary */
                            cc = fs.csize - csect;
                        }
                        if (disk_read(fs.pdrv, rbuff, sect, cc) != RES_OK) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                        if ((fp.flag & 0x80) != 0 && fp.sect - sect < cc)
                        {
                            memcpy(rbuff + checked((UINT)((fp.sect - sect) * (fs.ssize))), fp.buf, (fs.ssize));
                        }
                        rcnt = (fs.ssize) * cc; /* Number of bytes transferred */
                        continue;
                    }
                    if (fp.sect != sect)
                    { /* Load data sector if not in cache */
                        if ((fp.flag & 0x80) != 0)
                        { /* Write-back dirty sector cache */
                            if (disk_write(fs.pdrv, fp.buf, fp.sect, 1) != RES_OK) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                            fp.flag &= unchecked((BYTE)~0x80);
                        }
                        if (disk_read(fs.pdrv, fp.buf, sect, 1) != RES_OK) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; }; /* Fill sector cache */
                    }
                    fp.sect = sect;
                }
                rcnt = (fs.ssize) - (UINT)fp.fptr % (fs.ssize); /* Number of bytes remains in the sector */
                if (rcnt > btr) rcnt = btr; /* Clip it by btr if needed */
                memcpy(rbuff, fp.buf + checked((UINT)(fp.fptr % (fs.ssize))), rcnt); /* Extract partial sector */
            }
            return FR_OK;
        }
        /*-----------------------------------------------------------------------*/
        /* Write File                                                            */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_write(
         FIL fp, /* Open file to be written */
        ReadOnlySpan<byte> buffSpan, /* Data to be written */
        out UINT bw /* Number of bytes written */
        )
        {
            ReadOnlyPtr<byte> buff = buffSpan;
            FRESULT res;
            FATFS fs;
            DWORD clst;
            LBA_t sect;
            UINT wcnt, cc, csect;
            ReadOnlyPtr<BYTE> wbuff = buff;
            UINT btw = (UINT)buff.Span.Length;
            bw = 0; /* Clear write byte counter */
            res = validate(ref fp.obj, out fs); /* Check validity of the file object */
            if (res != FR_OK || (res = (FRESULT)fp.err) != FR_OK) return res; /* Check validity */
            if ((fp.flag & 0x02) == 0) return FR_DENIED; /* Check access mode */
            /* Check fptr wrap-around (file size cannot reach 4 GiB at FAT volume) */
            if ((false || fs.fs_type != 4) && (DWORD)(fp.fptr + btw) < (DWORD)fp.fptr)
            {
                btw = (UINT)(0xFFFFFFFF - (DWORD)fp.fptr);
            }
            for (; btw > 0; btw -= wcnt, bw += wcnt, wbuff += wcnt, fp.fptr += wcnt, fp.obj.objsize = (fp.fptr > fp.obj.objsize) ? fp.fptr : fp.obj.objsize)
            { /* Repeat until all data written */
                if (fp.fptr % (fs.ssize) == 0)
                { /* On the sector boundary? */
                    csect = (UINT)(fp.fptr / ((UINT)fs.ssize)) & ((UINT)fs.csize - 1); /* Sector offset in the cluster */
                    if (csect == 0)
                    { /* On the cluster boundary? */
                        if (fp.fptr == 0)
                        { /* On the top of the file? */
                            clst = fp.obj.sclust; /* Follow from the origin */
                            if (clst == 0)
                            { /* If no cluster is allocated, */
                                clst = create_chain(ref fp.obj, 0); /* create a new cluster chain */
                            }
                        }
                        else
                        { /* On the middle or end of the file */
                            {
                                clst = create_chain(ref fp.obj, fp.clust); /* Follow or stretch cluster chain on the FAT */
                            }
                        }
                        if (clst == 0) break; /* Could not allocate a new cluster (disk full) */
                        if (clst == 1) { fp.err = (BYTE)(FR_INT_ERR); return FR_INT_ERR; };
                        if (clst == 0xFFFFFFFF) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                        fp.clust = clst; /* Update current cluster */
                        if (fp.obj.sclust == 0) fp.obj.sclust = clst; /* Set start cluster if the first write */
                    }
                    if ((fp.flag & 0x80) != 0)
                    { /* Write-back sector cache */
                        if (disk_write(fs.pdrv, fp.buf, fp.sect, 1) != RES_OK) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                        fp.flag &= unchecked((BYTE)~0x80);
                    }
                    sect = clst2sect(fs, fp.clust); /* Get current sector */
                    if (sect == 0) { fp.err = (BYTE)(FR_INT_ERR); return FR_INT_ERR; };
                    sect += csect;
                    cc = btw / (fs.ssize); /* When remaining bytes >= sector size, */
                    if (cc > 0)
                    { /* Write maximum contiguous sectors directly */
                        if (csect + cc > fs.csize)
                        { /* Clip at cluster boundary */
                            cc = fs.csize - csect;
                        }
                        if (disk_write(fs.pdrv, wbuff, sect, cc) != RES_OK) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                        if (fp.sect - sect < cc)
                        { /* Refill sector cache if it gets invalidated by the direct write */
                            memcpy(fp.buf, wbuff + checked((UINT)((fp.sect - sect) * (fs.ssize))), (fs.ssize));
                            fp.flag &= unchecked((BYTE)~0x80);
                        }
                        wcnt = (fs.ssize) * cc; /* Number of bytes transferred */
                        continue;
                    }
                    if (fp.sect != sect && /* Fill sector cache with file data */
                     fp.fptr < fp.obj.objsize &&
                     disk_read(fs.pdrv, fp.buf, sect, 1) != RES_OK)
                    {
                        { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                    }
                    fp.sect = sect;
                }
                wcnt = (fs.ssize) - (UINT)fp.fptr % (fs.ssize); /* Number of bytes remains in the sector */
                if (wcnt > btw) wcnt = btw; /* Clip it by btw if needed */
                memcpy(fp.buf + checked((UINT)(fp.fptr % (fs.ssize))), wbuff, wcnt); /* Fit data to the sector */
                fp.flag |= 0x80;
            }
            fp.flag |= 0x40; /* Set file change flag */
            return FR_OK;
        }
        /*-----------------------------------------------------------------------*/
        /* Synchronize the File                                                  */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_sync(
         FIL fp /* Open file to be synced */
        )
        {
            FRESULT res;
            FATFS fs;
            DWORD tm;
            Ptr<BYTE> dir;
            res = validate(ref fp.obj, out fs); /* Check validity of the file object */
            if (res == FR_OK)
            {
                if ((fp.flag & 0x40) != 0)
                { /* Is there any change to the file? */
                    if ((fp.flag & 0x80) != 0)
                    { /* Write-back cached data if needed */
                        if (disk_write(fs.pdrv, fp.buf, fp.sect, 1) != RES_OK) return FR_DISK_ERR;
                        fp.flag &= unchecked((BYTE)~0x80);
                    }
                    /* Update the directory entry */
                    tm = get_fattime(); /* Modified time */
                    if (fs.fs_type == 4)
                    {
                        res = fill_first_frag(ref fp.obj); /* Fill first fragment on the FAT if needed */
                        if (res == FR_OK)
                        {
                            res = fill_last_frag(ref fp.obj, fp.clust, 0xFFFFFFFF); /* Fill last fragment on the FAT if needed */
                        }
                        if (res == FR_OK)
                        {
                            DIR dj = new();
                            fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                            fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                            res = load_obj_xdir(dj, ref fp.obj); /* Load directory entry block */
                            if (res == FR_OK)
                            {
                                fs.dirbuf[4] |= 0x20; /* Set archive attribute to indicate that the file has been changed */
                                fs.dirbuf[33] = (BYTE)(fp.obj.stat | 1); /* Update file allocation information */
                                st_dword(fs.dirbuf.AsSpan(52), fp.obj.sclust); /* Update start cluster */
                                st_qword(fs.dirbuf.AsSpan(56), fp.obj.objsize); /* Update file size */
                                st_qword(fs.dirbuf.AsSpan(40), fp.obj.objsize); /* (FatFs does not support Valid File Size feature) */
                                st_dword(fs.dirbuf.AsSpan(12), tm); /* Update modified time */
                                fs.dirbuf[21] = 0;
                                st_dword(fs.dirbuf.AsSpan(16), 0);
                                res = store_xdir(dj); /* Restore it to the directory */
                                if (res == FR_OK)
                                {
                                    res = sync_fs(fs);
                                    fp.flag &= unchecked((BYTE)~0x40);
                                }
                            }
                            ArrayPool<WCHAR>.Shared.Return(fs.lfnbuf);
                            ArrayPool<BYTE>.Shared.Return(fs.dirbuf);
                            fs.lfnbuf = null!;
                            fs.dirbuf = null!;
                        }
                    }
                    else
                    {
                        res = move_window(fs, fp.dir_sect);
                        if (res == FR_OK)
                        {
                            dir = fp.dir;
                            dir[11] |= 0x20; /* Set archive attribute to indicate that the file has been changed */
                            st_clust(fp.obj.fs, dir, fp.obj.sclust); /* Update file allocation information  */
                            st_dword(dir + 28, (DWORD)fp.obj.objsize); /* Update file size */
                            st_dword(dir + 22, tm); /* Update modified time */
                            st_word(dir + 18, 0);
                            fs.wflag = 1;
                            res = sync_fs(fs); /* Restore it to the directory */
                            fp.flag &= unchecked((BYTE)~0x40);
                        }
                    }
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Close File                                                            */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_close(
         FIL fp /* Open file to be closed */
        )
        {
            FRESULT res;
            FATFS fs;
            res = f_sync(fp); /* Flush cached data */
            if (res == FR_OK)
            {
                res = validate(ref fp.obj, out fs); /* Lock volume */
                if (res == FR_OK)
                {
                    fp.obj.fs = null!; /* Invalidate file object */
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Seek File Read/Write Pointer                                          */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_lseek(
         FIL fp, /* Pointer to the file object */
         FSIZE_t ofs /* File pointer from top of file */
        )
        {
            FRESULT res;
            FATFS fs;
            DWORD clst, bcs;
            LBA_t nsect;
            FSIZE_t ifptr;
            res = validate(ref fp.obj, out fs); /* Check validity of the file object */
            if (res == FR_OK) res = (FRESULT)fp.err;
            if (res == FR_OK && fs.fs_type == 4)
            {
                res = fill_last_frag(ref fp.obj, fp.clust, 0xFFFFFFFF); /* Fill last fragment on the FAT if needed */
            }
            if (res != FR_OK) return res;
            /* Normal Seek */
            {
                if (fs.fs_type != 4 && ofs >= 0x100000000) ofs = 0xFFFFFFFF; /* Clip at 4 GiB - 1 if at FATxx */
                if (ofs > fp.obj.objsize && (false || (fp.flag & 0x02) == 0))
                { /* In read-only mode, clip offset with the file size */
                    ofs = fp.obj.objsize;
                }
                ifptr = fp.fptr;
                fp.fptr = nsect = 0;
                if (ofs > 0)
                {
                    bcs = (DWORD)fs.csize * (fs.ssize); /* Cluster size (byte) */
                    if (ifptr > 0 &&
        (ofs - 1) / bcs >= (ifptr - 1) / bcs)
                    { /* When seek to same or following cluster, */
                        fp.fptr = (ifptr - 1) & ~(FSIZE_t)(bcs - 1); /* start from the current cluster */
                        ofs -= fp.fptr;
                        clst = fp.clust;
                    }
                    else
                    { /* When seek to back cluster, */
                        clst = fp.obj.sclust; /* start from the first cluster */
                        if (clst == 0)
                        { /* If no cluster chain, create a new chain */
                            clst = create_chain(ref fp.obj, 0);
                            if (clst == 1) { fp.err = (BYTE)(FR_INT_ERR); return FR_INT_ERR; };
                            if (clst == 0xFFFFFFFF) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                            fp.obj.sclust = clst;
                        }
                        fp.clust = clst;
                    }
                    if (clst != 0)
                    {
                        while (ofs > bcs)
                        { /* Cluster following loop */
                            ofs -= bcs; fp.fptr += bcs;
                            if ((fp.flag & 0x02) != 0)
                            { /* Check if in write mode or not */
                                if (true && fp.fptr > fp.obj.objsize)
                                { /* No FAT chain object needs correct objsize to generate FAT value */
                                    fp.obj.objsize = fp.fptr;
                                    fp.flag |= 0x40;
                                }
                                clst = create_chain(ref fp.obj, clst); /* Follow chain with forceed stretch */
                                if (clst == 0)
                                { /* Clip file size in case of disk full */
                                    ofs = 0; break;
                                }
                            }
                            else
                            {
                                clst = get_fat(ref fp.obj, clst); /* Follow cluster chain if not in write mode */
                            }
                            if (clst == 0xFFFFFFFF) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                            if (clst <= 1 || clst >= fs.n_fatent) { fp.err = (BYTE)(FR_INT_ERR); return FR_INT_ERR; };
                            fp.clust = clst;
                        }
                        fp.fptr += ofs;
                        if (ofs % (fs.ssize) != 0)
                        {
                            nsect = clst2sect(fs, clst); /* Current sector */
                            if (nsect == 0) { fp.err = (BYTE)(FR_INT_ERR); return FR_INT_ERR; };
                            nsect += (DWORD)(ofs / (fs.ssize));
                        }
                    }
                }
                if (true && fp.fptr > fp.obj.objsize)
                { /* Set file change flag if the file size is extended */
                    fp.obj.objsize = fp.fptr;
                    fp.flag |= 0x40;
                }
                if (fp.fptr % (fs.ssize) != 0 && nsect != fp.sect)
                { /* Fill sector cache if needed */
                    if ((fp.flag & 0x80) != 0)
                    { /* Write-back dirty sector cache */
                        if (disk_write(fs.pdrv, fp.buf, fp.sect, 1) != RES_OK) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; };
                        fp.flag &= unchecked((BYTE)~0x80);
                    }
                    if (disk_read(fs.pdrv, fp.buf, nsect, 1) != RES_OK) { fp.err = (BYTE)(FR_DISK_ERR); return FR_DISK_ERR; }; /* Fill sector cache */
                    fp.sect = nsect;
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Create a Directory Object                                             */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_opendir(
            out DIR dp, /* Pointer to directory object to create */
            ReadOnlySpan<WCHAR> pathSpan /* Pointer to the directory path */
        )
        {
            FRESULT res;
            FATFS fs;
            ReadOnlyPtr<WCHAR> path = pathSpan;
            dp = new();
            /* Get logical drive */
            res = mount_volume(ref path, out fs, 0);
            if (res == FR_OK)
            {
                dp.obj.fs = fs;
                fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(dp, path); /* Follow the path to the directory */
                if (res == FR_OK)
                { /* Follow completed */
                    if ((dp.fn[11] & 0x80) == 0)
                    { /* It is not the origin directory itself */
                        if ((dp.obj.attr & 0x10) != 0)
                        { /* This object is a sub-directory */
                            if (fs.fs_type == 4)
                            {
                                dp.obj.c_scl = dp.obj.sclust; /* Get containing directory information */
                                dp.obj.c_size = ((DWORD)dp.obj.objsize & 0xFFFFFF00) | dp.obj.stat;
                                dp.obj.c_ofs = dp.blk_ofs;
                                init_alloc_info(fs, ref dp.obj); /* Get object allocation info */
                            }
                            else
                            {
                                dp.obj.sclust = ld_clust(fs, dp.dir); /* Get object allocation info */
                            }
                        }
                        else
                        { /* This object is a file */
                            res = FR_NO_PATH;
                        }
                    }
                    if (res == FR_OK)
                    {
                        dp.obj.id = fs.id;
                        res = dir_sdi(dp, 0); /* Rewind directory */
                    }
                }
                ArrayPool<WCHAR>.Shared.Return(fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(fs.dirbuf);
                fs.lfnbuf = null!;
                fs.dirbuf = null!;
                if (res == FR_NO_FILE) res = FR_NO_PATH;
            }
            if (res != FR_OK) dp.obj.fs = null!; /* Invalidate the directory object if function failed */
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Close Directory                                                       */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_closedir(
         DIR dp /* Pointer to the directory object to be closed */
        )
        {
            FRESULT res;
            FATFS fs;
            res = validate(ref dp.obj, out fs); /* Check validity of the file object */
            if (res == FR_OK)
            {
                dp.obj.fs = null!; /* Invalidate directory object */
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Read Directory Entries in Sequence                                    */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_readdir(
         DIR dp, /* Pointer to the open directory object */
         out FILINFO fno /* Pointer to file information to return */
        )
        {
            FRESULT res;
            FATFS fs;
            fno = new();
            res = validate(ref dp.obj, out fs); /* Check validity of the directory object */
            if (res == FR_OK)
            {
                //if (!fno)
                //{
                //    res = dir_sdi(dp, 0); /* Rewind the directory object */
                //}
                //else
                {
                    fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                    fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                    res = dir_read(dp, 0); /* Read an item */
                    if (res == FR_NO_FILE) res = FR_OK; /* Ignore end of directory */
                    if (res == FR_OK)
                    { /* A valid entry is found */
                        get_fileinfo(dp, fno); /* Get the object information */
                        res = dir_next(dp, 0); /* Increment index for next */
                        if (res == FR_NO_FILE) res = FR_OK; /* Ignore end of directory now */
                    }
                    ArrayPool<WCHAR>.Shared.Return(fs.lfnbuf);
                    ArrayPool<BYTE>.Shared.Return(fs.dirbuf);
                    fs.lfnbuf = null!;
                    fs.dirbuf = null!;
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Get File Status                                                       */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_stat(
         ReadOnlyPtr<TCHAR> path, /* Pointer to the file path */
         out FILINFO fno /* Pointer to file information to return */
        )
        {
            FRESULT res;
            DIR dj = new();
            fno = new();
            /* Get logical drive */
            res = mount_volume(ref path, out dj.obj.fs, 0);
            if (res == FR_OK)
            {
                dj.obj.fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                dj.obj.fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(dj, path); /* Follow the file path */
                if (res == FR_OK)
                { /* Follow completed */
                    if ((dj.fn[11] & 0x80) != 0)
                    { /* It is origin directory */
                        res = FR_INVALID_NAME;
                    }
                    else
                    { /* Found an object */
                        get_fileinfo(dj, fno);
                    }
                }
                ArrayPool<WCHAR>.Shared.Return(dj.obj.fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(dj.obj.fs.dirbuf);
                dj.obj.fs.lfnbuf = null!;
                dj.obj.fs.dirbuf = null!;
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Get Number of Free Clusters                                           */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_getfree(
         ReadOnlySpan<WCHAR> pathSpan, /* Logical drive number */
         out DWORD nclst, /* Pointer to a variable to return number of free clusters */
         out FATFS fatfs /* Pointer to a pointer to return corresponding filesystem object */
        )
        {
            ReadOnlyPtr<WCHAR> path = pathSpan;
            FRESULT res;
            FATFS fs;
            DWORD nfree, clst, stat;
            LBA_t sect;
            UINT i;
            FFOBJID obj = new();
            nclst = 0;
            fatfs = null!;
            /* Get logical drive */
            res = mount_volume(ref path, out fs, 0);
            if (res == FR_OK)
            {
                fatfs = fs; /* Return ptr to the fs object */
                /* If free_clst is valid, return it without full FAT scan */
                if (fs.free_clst <= fs.n_fatent - 2)
                {
                    nclst = fs.free_clst;
                }
                else
                {
                    /* Scan FAT to obtain the correct free cluster count */
                    nfree = 0;
                    if (fs.fs_type == 1)
                    { /* FAT12: Scan bit field FAT entries */
                        clst = 2; obj.fs = fs;
                        do
                        {
                            stat = get_fat(ref obj, clst);
                            if (stat == 0xFFFFFFFF)
                            {
                                res = FR_DISK_ERR; break;
                            }
                            if (stat == 1)
                            {
                                res = FR_INT_ERR; break;
                            }
                            if (stat == 0) nfree++;
                        } while (++clst < fs.n_fatent);
                    }
                    else
                    {
                        if (fs.fs_type == 4)
                        { /* exFAT: Scan allocation bitmap */
                            BYTE bm;
                            UINT b;
                            clst = fs.n_fatent - 2; /* Number of clusters */
                            sect = fs.bitbase; /* Bitmap sector */
                            i = 0; /* Offset in the sector */
                            do
                            { /* Counts numbuer of clear bits (free clusters) in the bitmap */
                                if (i == 0)
                                { /* New sector? */
                                    res = move_window(fs, sect++);
                                    if (res != FR_OK) break;
                                }
                                for (b = 8, bm = (BYTE)(~fs.win[i]); b != 0 && clst != 0; b--, clst--)
                                { /* Count clear bits in a byte */
                                    nfree += (UINT)(bm & 1);
                                    bm >>= 1;
                                }
                                i = (i + 1) % (fs.ssize); /* Next byte */
                            } while (clst != 0);
                        }
                        else
                        { /* FAT16/32: Scan WORD/DWORD FAT entries */
                            clst = fs.n_fatent; /* Number of entries */
                            sect = fs.fatbase; /* Top of the FAT */
                            i = 0; /* Offset in the sector */
                            do
                            { /* Counts numbuer of entries with zero in the FAT */
                                if (i == 0)
                                { /* New sector? */
                                    res = move_window(fs, sect++);
                                    if (res != FR_OK) break;
                                }
                                if (fs.fs_type == 2)
                                {
                                    if (ld_word(fs.win + i) == 0) nfree++; /* FAT16: Is this cluster free? */
                                    i += 2; /* Next entry */
                                }
                                else
                                {
                                    if ((ld_dword(fs.win + i) & 0x0FFFFFFF) == 0) nfree++; /* FAT32: Is this cluster free? */
                                    i += 4; /* Next entry */
                                }
                                i %= (fs.ssize);
                            } while ((--clst) != 0);
                        }
                    }
                    if (res == FR_OK)
                    { /* Update parameters if succeeded */
                        nclst = nfree; /* Return the free clusters */
                        fs.free_clst = nfree; /* Now free cluster count is valid */
                        fs.fsi_flag |= 1; /* FAT32/exfAT : Allocation information is to be updated */
                    }
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Truncate File                                                         */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_truncate(
         FIL fp /* Pointer to the file object */
        )
        {
            FRESULT res;
            FATFS fs;
            DWORD ncl;
            res = validate(ref fp.obj, out fs); /* Check validity of the file object */
            if (res != FR_OK || (res = (FRESULT)fp.err) != FR_OK) return res;
            if ((fp.flag & 0x02) == 0) return FR_DENIED; /* Check access mode */
            if (fp.fptr < fp.obj.objsize)
            { /* Process when fptr is not on the eof */
                if (fp.fptr == 0)
                { /* When set file size to zero, remove entire cluster chain */
                    res = remove_chain(ref fp.obj, fp.obj.sclust, 0);
                    fp.obj.sclust = 0;
                }
                else
                { /* When truncate a part of the file, remove remaining clusters */
                    ncl = get_fat(ref fp.obj, fp.clust);
                    res = FR_OK;
                    if (ncl == 0xFFFFFFFF) res = FR_DISK_ERR;
                    if (ncl == 1) res = FR_INT_ERR;
                    if (res == FR_OK && ncl < fs.n_fatent)
                    {
                        res = remove_chain(ref fp.obj, ncl, fp.clust);
                    }
                }
                fp.obj.objsize = fp.fptr; /* Set file size to current read/write point */
                fp.flag |= 0x40;
                if (res == FR_OK && (fp.flag & 0x80) != 0)
                {
                    if (disk_write(fs.pdrv, fp.buf, fp.sect, 1) != RES_OK)
                    {
                        res = FR_DISK_ERR;
                    }
                    else
                    {
                        fp.flag &= unchecked((BYTE)~0x80);
                    }
                }
                if (res != FR_OK) { fp.err = (BYTE)(res); return res; };
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Delete a File/Directory                                               */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_unlink(
         ReadOnlySpan<WCHAR> pathSpan /* Pointer to the file or directory path */
        )
        {
            ReadOnlyPtr<WCHAR> path = pathSpan;
            FRESULT res;
            FATFS fs;
            DIR dj = new(), sdj = new();
            DWORD dclst = 0;
            FFOBJID obj = new();
            /* Get logical drive */
            res = mount_volume(ref path, out fs, 0x02);
            if (res == FR_OK)
            {
                dj.obj.fs = fs;
                dj.obj.fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                dj.obj.fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(dj, path); /* Follow the file path */
                if (false && res == FR_OK && (dj.fn[11] & 0x20) != 0)
                {
                    res = FR_INVALID_NAME; /* Cannot remove dot entry */
                }
                if (res == FR_OK)
                { /* The object is accessible */
                    if ((dj.fn[11] & 0x80) != 0)
                    {
                        res = FR_INVALID_NAME; /* Cannot remove the origin directory */
                    }
                    else
                    {
                        if ((dj.obj.attr & 0x01) != 0)
                        {
                            res = FR_DENIED; /* Cannot remove R/O object */
                        }
                    }
                    if (res == FR_OK)
                    {
                        obj.fs = fs;
                        if (fs.fs_type == 4)
                        {
                            init_alloc_info(fs, ref obj);
                            dclst = obj.sclust;
                        }
                        else
                        {
                            dclst = ld_clust(fs, dj.dir);
                        }
                        if ((dj.obj.attr & 0x10) != 0)
                        { /* Is it a sub-directory? */
                            {
                                sdj.obj.fs = fs; /* Open the sub-directory */
                                sdj.obj.sclust = dclst;
                                if (fs.fs_type == 4)
                                {
                                    sdj.obj.objsize = obj.objsize;
                                    sdj.obj.stat = obj.stat;
                                }
                                res = dir_sdi(sdj, 0);
                                if (res == FR_OK)
                                {
                                    res = dir_read(sdj, 0); /* Test if the directory is empty */
                                    if (res == FR_OK) res = FR_DENIED; /* Not empty? */
                                    if (res == FR_NO_FILE) res = FR_OK; /* Empty? */
                                }
                            }
                        }
                    }
                    if (res == FR_OK)
                    {
                        res = dir_remove(dj); /* Remove the directory entry */
                        if (res == FR_OK && dclst != 0)
                        { /* Remove the cluster chain if exist */
                            res = remove_chain(ref obj, dclst, 0);
                        }
                        if (res == FR_OK) res = sync_fs(fs);
                    }
                }
                ArrayPool<WCHAR>.Shared.Return(dj.obj.fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(dj.obj.fs.dirbuf);
                dj.obj.fs.lfnbuf = null!;
                dj.obj.fs.dirbuf = null!;
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Create a Directory                                                    */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_mkdir(
         ReadOnlySpan<WCHAR> pathSpan /* Pointer to the directory path */
        )
        {
            ReadOnlyPtr<TCHAR> path = pathSpan;
            FRESULT res;
            FATFS fs;
            DIR dj = new();
            FFOBJID sobj = new();
            DWORD dcl, pcl, tm;
            res = mount_volume(ref path, out fs, 0x02); /* Get logical drive */
            if (res == FR_OK)
            {
                dj.obj.fs = fs;
                dj.obj.fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                dj.obj.fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(dj, path); /* Follow the file path */
                if (res == FR_OK) res = FR_EXIST; /* Name collision? */
                if (false && res == FR_NO_FILE && (dj.fn[11] & 0x20) != 0)
                { /* Invalid name? */
                    res = FR_INVALID_NAME;
                }
                if (res == FR_NO_FILE)
                { /* It is clear to create a new directory */
                    sobj.fs = fs; /* New object id to create a new chain */
                    dcl = create_chain(ref sobj, 0); /* Allocate a cluster for the new directory */
                    res = FR_OK;
                    if (dcl == 0) res = FR_DENIED; /* No space to allocate a new cluster? */
                    if (dcl == 1) res = FR_INT_ERR; /* Any insanity? */
                    if (dcl == 0xFFFFFFFF) res = FR_DISK_ERR; /* Disk error? */
                    tm = get_fattime();
                    if (res == FR_OK)
                    {
                        res = dir_clear(fs, dcl); /* Clean up the new table */
                        if (res == FR_OK)
                        {
                            if (false || fs.fs_type != 4)
                            { /* Create dot entries (FAT only) */
                                memset(fs.win + 0, (byte)' ', 11); /* Create "." entry */
                                fs.win[0] = (byte)'.';
                                fs.win[11] = 0x10;
                                st_dword(fs.win + 22, tm);
                                st_clust(fs, fs.win, dcl);
                                memcpy(fs.win + 32, fs.win, 32); /* Create ".." entry */
                                fs.win[32 + 1] = (byte)'.'; pcl = dj.obj.sclust;
                                st_clust(fs, fs.win + 32, pcl);
                                fs.wflag = 1;
                            }
                            res = dir_register(dj); /* Register the object to the parent directory */
                        }
                    }
                    if (res == FR_OK)
                    {
                        if (fs.fs_type == 4)
                        { /* Initialize directory entry block */
                            st_dword(fs.dirbuf.AsSpan(12), tm); /* Created time */
                            st_dword(fs.dirbuf.AsSpan(52), dcl); /* Table start cluster */
                            st_dword(fs.dirbuf.AsSpan(56), (DWORD)fs.csize * (fs.ssize)); /* Directory size needs to be valid */
                            st_dword(fs.dirbuf.AsSpan(40), (DWORD)fs.csize * (fs.ssize));
                            fs.dirbuf[33] = 3; /* Initialize the object flag */
                            fs.dirbuf[4] = 0x10; /* Attribute */
                            res = store_xdir(dj);
                        }
                        else
                        {
                            st_dword(dj.dir + 22, tm); /* Created time */
                            st_clust(fs, dj.dir, dcl); /* Table start cluster */
                            dj.dir[11] = 0x10; /* Attribute */
                            fs.wflag = 1;
                        }
                        if (res == FR_OK)
                        {
                            res = sync_fs(fs);
                        }
                    }
                    else
                    {
                        remove_chain(ref sobj, dcl, 0); /* Could not register, remove the allocated cluster */
                    }
                }
                ArrayPool<WCHAR>.Shared.Return(dj.obj.fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(dj.obj.fs.dirbuf);
                dj.obj.fs.lfnbuf = null!;
                dj.obj.fs.dirbuf = null!;
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Rename a File/Directory                                               */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_rename(
         ReadOnlySpan<TCHAR> path_old_span, /* Pointer to the object name to be renamed */
         ReadOnlySpan<TCHAR> path_new_span /* Pointer to the new name */
        )
        {
            ReadOnlyPtr<TCHAR> path_old = path_old_span;
            ReadOnlyPtr<TCHAR> path_new = path_new_span;
            FRESULT res;
            FATFS fs;
            DIR djo = new(), djn;
            Span<BYTE> bufSpan = stackalloc BYTE[true ? 32 * 2 : 32];
            Ptr<Byte> buf = bufSpan, dir;
            LBA_t sect;
            get_ldnumber(ref path_new); /* Snip the drive number of new name off */
            res = mount_volume(ref path_old, out fs, 0x02); /* Get logical drive of the old object */
            if (res == FR_OK)
            {
                djo.obj.fs = fs;
                djo.obj.fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                djo.obj.fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(djo, path_old); /* Check old object */
                if (res == FR_OK && (djo.fn[11] & (0x20 | 0x80)) != 0) res = FR_INVALID_NAME; /* Check validity of name */
                if (res == FR_OK)
                { /* Object to be renamed is found */
                    if (fs.fs_type == 4)
                    { /* At exFAT volume */
                        BYTE nf, nn;
                        WORD nh;
                        memcpy(buf, fs.dirbuf, 32 * 2); /* Save 85+C0 entry of old object */
                        djn = djo.Clone();
                        res = follow_path(djn, path_new); /* Make sure if new object name is not in use */
                        if (res == FR_OK)
                        { /* Is new name already in use by any other object? */
                            res = (djn.obj.sclust == djo.obj.sclust && djn.dptr == djo.dptr) ? FR_NO_FILE : FR_EXIST;
                        }
                        if (res == FR_NO_FILE)
                        { /* It is a valid path and no name collision */
                            res = dir_register(djn); /* Register the new entry */
                            if (res == FR_OK)
                            {
                                nf = fs.dirbuf[1]; nn = fs.dirbuf[35];
                                nh = ld_word(fs.dirbuf.AsSpan(36));
                                memcpy(fs.dirbuf, buf, 32 * 2); /* Restore 85+C0 entry */
                                fs.dirbuf[1] = nf; fs.dirbuf[35] = nn;
                                st_word(fs.dirbuf.AsSpan(36), nh);
                                if ((fs.dirbuf[4] & 0x10) == 0) fs.dirbuf[4] |= 0x20; /* Set archive attribute if it is a file */
                                /* Start of critical section where an interruption can cause a cross-link */
                                res = store_xdir(djn);
                            }
                        }
                    }
                    else
                    { /* At FAT/FAT32 volume */
                        memcpy(buf, djo.dir, 32); /* Save directory entry of the object */
                        djn = djo.Clone();
                        res = follow_path(djn, path_new); /* Make sure if new object name is not in use */
                        if (res == FR_OK)
                        { /* Is new name already in use by any other object? */
                            res = (djn.obj.sclust == djo.obj.sclust && djn.dptr == djo.dptr) ? FR_NO_FILE : FR_EXIST;
                        }
                        if (res == FR_NO_FILE)
                        { /* It is a valid path and no name collision */
                            res = dir_register(djn); /* Register the new entry */
                            if (res == FR_OK)
                            {
                                dir = djn.dir; /* Copy directory entry of the object except name */
                                memcpy(dir + 13, buf + 13, 32 - 13);
                                dir[11] = buf[11];
                                if ((dir[11] & 0x10) == 0) dir[11] |= 0x20; /* Set archive attribute if it is a file */
                                fs.wflag = 1;
                                if ((dir[11] & 0x10) != 0 && djo.obj.sclust != djn.obj.sclust)
                                { /* Update .. entry in the sub-directory if needed */
                                    sect = clst2sect(fs, ld_clust(fs, dir));
                                    if (sect == 0)
                                    {
                                        res = FR_INT_ERR;
                                    }
                                    else
                                    {
                                        /* Start of critical section where an interruption can cause a cross-link */
                                        res = move_window(fs, sect);
                                        dir = fs.win + 32 * 1; /* Pointer to .. entry */
                                        if (res == FR_OK && dir[1] == '.')
                                        {
                                            st_clust(fs, dir, djn.obj.sclust);
                                            fs.wflag = 1;
                                        }
                                    }
                                }
                            }
                        }
                    }
                    if (res == FR_OK)
                    {
                        res = dir_remove(djo); /* Remove old entry */
                        if (res == FR_OK)
                        {
                            res = sync_fs(fs);
                        }
                    }
                    /* End of the critical section */
                }
                ArrayPool<WCHAR>.Shared.Return(djo.obj.fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(djo.obj.fs.dirbuf);
                djo.obj.fs.lfnbuf = null!;
                djo.obj.fs.dirbuf = null!;
            }
            return res;
        }

        /*-----------------------------------------------------------------------*/
        /* Change Attribute                                                      */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_chmod(
         ReadOnlyPtr<WCHAR> path, /* Pointer to the file path */
         BYTE attr, /* Attribute bits */
         BYTE mask /* Attribute mask to change */
        )
        {
            FRESULT res;
            FATFS fs;
            DIR dj = new();
            res = mount_volume(ref path, out fs, 0x02); /* Get logical drive */
            if (res == FR_OK)
            {
                dj.obj.fs = fs;
                dj.obj.fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                dj.obj.fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(dj, path); /* Follow the file path */
                if (res == FR_OK && (dj.fn[11] & (0x20 | 0x80)) != 0) res = FR_INVALID_NAME; /* Check object validity */
                if (res == FR_OK)
                {
                    mask &= 0x01 | 0x02 | 0x04 | 0x20; /* Valid attribute mask */
                    if (fs.fs_type == 4)
                    {
                        fs.dirbuf[4] = (BYTE)((attr & mask) | (fs.dirbuf[4] & (BYTE)~mask)); /* Apply attribute change */
                        res = store_xdir(dj);
                    }
                    else
                    {
                        dj.dir[11] = (BYTE)((attr & mask) | (dj.dir[11] & (BYTE)~mask)); /* Apply attribute change */
                        fs.wflag = 1;
                    }
                    if (res == FR_OK)
                    {
                        res = sync_fs(fs);
                    }
                }
                ArrayPool<WCHAR>.Shared.Return(dj.obj.fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(dj.obj.fs.dirbuf);
                dj.obj.fs.lfnbuf = null!;
                dj.obj.fs.dirbuf = null!;
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Change Timestamp                                                      */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_utime(
         ReadOnlyPtr<WCHAR> path, /* Pointer to the file/directory name */
         FILINFO fno /* Pointer to the timestamp to be set */
        )
        {
            FRESULT res;
            FATFS fs;
            DIR dj = new();
            res = mount_volume(ref path, out fs, 0x02); /* Get logical drive */
            if (res == FR_OK)
            {
                dj.obj.fs = fs;
                dj.obj.fs.lfnbuf = ArrayPool<WCHAR>.Shared.Rent(255 + 1);
                dj.obj.fs.dirbuf = ArrayPool<BYTE>.Shared.Rent((255 + 44) / 15 * 32);
                res = follow_path(dj, path); /* Follow the file path */
                if (res == FR_OK && (dj.fn[11] & (0x20 | 0x80)) != 0) res = FR_INVALID_NAME; /* Check object validity */
                if (res == FR_OK)
                {
                    if (fs.fs_type == 4)
                    {
                        st_dword(fs.dirbuf.AsSpan(12), (DWORD)fno.fdate << 16 | fno.ftime);
                        res = store_xdir(dj);
                    }
                    else
                    {
                        st_dword(dj.dir + 22, (DWORD)fno.fdate << 16 | fno.ftime);
                        fs.wflag = 1;
                    }
                    if (res == FR_OK)
                    {
                        res = sync_fs(fs);
                    }
                }
                ArrayPool<WCHAR>.Shared.Return(dj.obj.fs.lfnbuf);
                ArrayPool<BYTE>.Shared.Return(dj.obj.fs.dirbuf);
                dj.obj.fs.lfnbuf = null!;
                dj.obj.fs.dirbuf = null!;
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Get Volume Label                                                      */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_getlabel(
         ReadOnlyPtr<WCHAR> path, /* Logical drive number */
         Ptr<WCHAR> label, /* Buffer to store the volume label */
         out DWORD vsn /* Variable to store the volume serial number */
        )
        {
            FRESULT res;
            FATFS fs;
            DIR dj = new();
            UINT si, di;
            WCHAR wc;
            vsn = 0;
            /* Get logical drive */
            res = mount_volume(ref path, out fs, 0);
            /* Get volume label */
            if (res == FR_OK && !label.IsEmpty)
            {
                dj.obj.fs = fs; dj.obj.sclust = 0; /* Open root directory */
                res = dir_sdi(dj, 0);
                if (res == FR_OK)
                {
                    res = dir_read(dj, 1); /* Find a volume label entry */
                    if (res == FR_OK)
                    {
                        if (fs.fs_type == 4)
                        {
                            WCHAR hs;
                            UINT nw;
                            for (si = di = hs = (WCHAR)0; si < dj.dir[1]; si++)
                            { /* Extract volume label from 83 entry */
                                wc = (WCHAR)ld_word(dj.dir + 2 + si * 2);
                                if (hs == 0 && ((wc) >= 0xD800 && (wc) <= 0xDFFF))
                                { /* Is the code a surrogate? */
                                    hs = wc; continue;
                                }
                                nw = put_utf((DWORD)hs << 16 | wc, label + di, 4); /* Store it in API encoding */
                                if (nw == 0)
                                { /* Encode error? */
                                    di = 0; break;
                                }
                                di += nw;
                                hs = (WCHAR)0;
                            }
                            if (hs != 0) di = 0; /* Broken surrogate pair? */
                            label[di] = (WCHAR)0;
                        }
                        else
                        {
                            si = di = 0; /* Extract volume label from AM_VOL entry */
                            while (si < 11)
                            {
                                wc = (WCHAR)dj.dir[si++];
                                if (dbc_1st((BYTE)wc) != 0 && si < 11) wc = (WCHAR)(wc << 8 | dj.dir[si++]); /* Is it a DBC? */
                                wc = ff_oem2uni(wc); /* Convert it into Unicode */
                                if (wc == 0)
                                { /* Invalid char in current code page? */
                                    di = 0; break;
                                }
                                di += put_utf(wc, label + di, 4); /* Store it in Unicode */
                            }
                            do
                            { /* Truncate trailing spaces */
                                label[di] = (WCHAR)0;
                                if (di == 0) break;
                            } while (label[--di] == ' ');
                        }
                    }
                }
                if (res == FR_NO_FILE)
                { /* No label entry and return nul string */
                    label[0] = (WCHAR)0;
                    res = FR_OK;
                }
            }
            /* Get volume serial number */
            if (res == FR_OK)
            {
                res = move_window(fs, fs.volbase); /* Load VBR */
                if (res == FR_OK)
                {
                    switch (fs.fs_type)
                    {
                        case 4:
                            di = 100;
                            break;
                        case 3:
                            di = 67;
                            break;
                        default: /* FAT12/16 */
                            di = (UINT)(fs.win[38] == 0x29 ? 39 : 0);
                            break;
                    }
                    vsn = di != 0 ? ld_dword(fs.win + di) : 0; /* Get VSN in the VBR */
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Set Volume Label                                                      */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_setlabel(
         ReadOnlyPtr<WCHAR> label /* Volume label to set with heading logical drive number */
        )
        {
            FRESULT res;
            FATFS fs;
            DIR dj = new();
            Span<BYTE> dirvnSpan = stackalloc BYTE[22];
            Ptr<BYTE> dirvn = dirvnSpan;
            UINT di;
            WCHAR wc;
            DWORD dc;
            /* Get logical drive */
            res = mount_volume(ref label, out fs, 0x02);
            if (res != FR_OK) return res;
            if (fs.fs_type == 4)
            { /* On the exFAT volume */
                memset(dirvn, 0, 22);
                di = 0;
                while ((UINT)label.Value >= ' ')
                { /* Create volume label */
                    dc = tchar2uni(ref label); /* Get a Unicode character */
                    if (dc >= 0x10000)
                    {
                        if (dc == 0xFFFFFFFF || di >= 10)
                        { /* Wrong surrogate or buffer overflow */
                            dc = 0;
                        }
                        else
                        {
                            st_word(dirvn + di * 2, (WCHAR)(dc >> 16)); di++;
                        }
                    }
                    if (dc == 0 || strchr(_badchr.AsSpan(7), (WCHAR)dc) || di >= 11)
                    { /* Check validity of the volume label */
                        return FR_INVALID_NAME;
                    }
                    st_word(dirvn + di * 2, (WCHAR)dc); di++;
                }
            }
            else
            { /* On the FAT/FAT32 volume */
                memset(dirvn, (byte)' ', 11);
                di = 0;
                while ((UINT)label.Value >= ' ')
                { /* Create volume label */
                    dc = tchar2uni(ref label);
                    wc = (dc < 0x10000) ? ff_uni2oem(char.ToUpper((char)dc)) : (WCHAR)0;
                    if (wc == 0 || strchr(_badchr, (char)wc) || di >= (UINT)((wc >= 0x100) ? 10 : 11))
                    { /* Reject invalid characters for volume label */
                        return FR_INVALID_NAME;
                    }
                    if (wc >= 0x100) dirvn[di++] = (BYTE)(wc >> 8);
                    dirvn[di++] = (BYTE)wc;
                }
                if (dirvn[0] == 0xE5) return FR_INVALID_NAME; /* Reject illegal name (heading DDEM) */
                while (di != 0 && dirvn[di - 1] == ' ') di--; /* Snip trailing spaces */
            }
            /* Set volume label */
            dj.obj.fs = fs; dj.obj.sclust = 0; /* Open root directory */
            res = dir_sdi(dj, 0);
            if (res == FR_OK)
            {
                res = dir_read(dj, 1); /* Get volume label entry */
                if (res == FR_OK)
                {
                    if (true && fs.fs_type == 4)
                    {
                        dj.dir[1] = (BYTE)di; /* Change the volume label */
                        memcpy(dj.dir + 2, dirvn, 22);
                    }
                    else
                    {
                        if (di != 0)
                        {
                            memcpy(dj.dir, dirvn, 11); /* Change the volume label */
                        }
                        else
                        {
                            dj.dir[0] = 0xE5; /* Remove the volume label */
                        }
                    }
                    fs.wflag = 1;
                    res = sync_fs(fs);
                }
                else
                { /* No volume label entry or an error */
                    if (res == FR_NO_FILE)
                    {
                        res = FR_OK;
                        if (di != 0)
                        { /* Create a volume label entry */
                            res = dir_alloc(dj, 1); /* Allocate an entry */
                            if (res == FR_OK)
                            {
                                memset(dj.dir, 0, 32); /* Clean the entry */
                                if (true && fs.fs_type == 4)
                                {
                                    dj.dir[0] = 0x83; /* Create volume label entry */
                                    dj.dir[1] = (BYTE)di;
                                    memcpy(dj.dir + 2, dirvn, 22);
                                }
                                else
                                {
                                    dj.dir[11] = 0x08; /* Create volume label entry */
                                    memcpy(dj.dir, dirvn, 11);
                                }
                                fs.wflag = 1;
                                res = sync_fs(fs);
                            }
                        }
                    }
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Allocate a Contiguous Blocks to the File                              */
        /*-----------------------------------------------------------------------*/
        FRESULT f_expand(
         ref FIL fp, /* Pointer to the file object */
         FSIZE_t fsz, /* File size to be expanded to */
         BYTE opt /* Operation mode 0:Find and prepare or 1:Find and allocate */
        )
        {
            FRESULT res;
            FATFS fs;
            DWORD n, clst, stcl, scl, ncl, tcl, lclst;
            res = validate(ref fp.obj, out fs); /* Check validity of the file object */
            if (res != FR_OK || (res = (FRESULT)fp.err) != FR_OK) return res;
            if (fsz == 0 || fp.obj.objsize != 0 || (fp.flag & 0x02) == 0) return FR_DENIED;
            if (fs.fs_type != 4 && fsz >= 0x100000000) return FR_DENIED; /* Check if in size limit */
            n = (DWORD)fs.csize * (fs.ssize); /* Cluster size */
            tcl = (DWORD)(fsz / n) + ((fsz & (n - 1)) != 0 ? 1U : 0U); /* Number of clusters required */
            stcl = fs.last_clst; lclst = 0;
            if (stcl < 2 || stcl >= fs.n_fatent) stcl = 2;
            if (fs.fs_type == 4)
            {
                scl = find_bitmap(fs, stcl, tcl); /* Find a contiguous cluster block */
                if (scl == 0) res = FR_DENIED; /* No contiguous cluster block was found */
                if (scl == 0xFFFFFFFF) res = FR_DISK_ERR;
                if (res == FR_OK)
                { /* A contiguous free area is found */
                    if (opt != 0)
                    { /* Allocate it now */
                        res = change_bitmap(fs, scl, tcl, 1); /* Mark the cluster block 'in use' */
                        lclst = scl + tcl - 1;
                    }
                    else
                    { /* Set it as suggested point for next allocation */
                        lclst = scl - 1;
                    }
                }
            }
            else
            {
                scl = clst = stcl; ncl = 0;
                for (; ; )
                { /* Find a contiguous cluster block */
                    n = get_fat(ref fp.obj, clst);
                    if (++clst >= fs.n_fatent) clst = 2;
                    if (n == 1)
                    {
                        res = FR_INT_ERR; break;
                    }
                    if (n == 0xFFFFFFFF)
                    {
                        res = FR_DISK_ERR; break;
                    }
                    if (n == 0)
                    { /* Is it a free cluster? */
                        if (++ncl == tcl) break; /* Break if a contiguous cluster block is found */
                    }
                    else
                    {
                        scl = clst; ncl = 0; /* Not a free cluster */
                    }
                    if (clst == stcl)
                    { /* No contiguous cluster? */
                        res = FR_DENIED; break;
                    }
                }
                if (res == FR_OK)
                { /* A contiguous free area is found */
                    if (opt != 0)
                    { /* Allocate it now */
                        for (clst = scl, n = tcl; n != 0; clst++, n--)
                        { /* Create a cluster chain on the FAT */
                            res = put_fat(fs, clst, (n == 1) ? 0xFFFFFFFF : clst + 1);
                            if (res != FR_OK) break;
                            lclst = clst;
                        }
                    }
                    else
                    { /* Set it as suggested point for next allocation */
                        lclst = scl - 1;
                    }
                }
            }
            if (res == FR_OK)
            {
                fs.last_clst = lclst; /* Set suggested start cluster to start next */
                if (opt != 0)
                { /* Is it allocated now? */
                    fp.obj.sclust = scl; /* Update object allocation information */
                    fp.obj.objsize = fsz;
                    if (true) fp.obj.stat = 2; /* Set status 'contiguous chain' */
                    fp.flag |= 0x40;
                    if (fs.free_clst <= fs.n_fatent - 2)
                    { /* Update FSINFO */
                        fs.free_clst -= tcl;
                        fs.fsi_flag |= 1;
                    }
                }
            }
            return res;
        }
        /*-----------------------------------------------------------------------*/
        /* Create FAT/exFAT volume (with sub-functions)                          */
        /*-----------------------------------------------------------------------*/
        /* Create partitions on the physical drive in format of MBR or GPT */
        public FRESULT create_partition(
         BYTE drv, /* Physical drive number */
        Ptr<LBA_t> plst, /* Partition list */
        BYTE sys, /* System ID for each partition (for only MBR) */
        Ptr<BYTE> buf /* Working buffer for a sector */
        )
        {
            UINT i, cy;
            LBA_t sz_drv = 0;
            DWORD sz_drv32, nxt_alloc32, sz_part32;
            Ptr<BYTE> pte;
            BYTE hd, n_hd, sc, n_sc;
            /* Get physical drive size */
            if (disk_ioctl(drv, 1, MemoryMarshal.AsBytes(new Span<LBA_t>(ref sz_drv))) != RES_OK) return FR_DISK_ERR;
            if (sz_drv >= 0x10000000)
            { /* Create partitions in GPT format */
                WORD ss = 0;
                UINT sz_ptbl, pi, si, ofs;
                DWORD bcc, rnd, align;
                QWORD nxt_alloc, sz_part, sz_pool, top_bpt;
                if (disk_ioctl(drv, 2, MemoryMarshal.AsBytes(new Span<WORD>(ref ss))) != RES_OK) return FR_DISK_ERR; /* Get sector size */
                if (ss > 4096 || ss < 512 || (ss & (ss - 1)) != 0) return FR_DISK_ERR;
                rnd = (DWORD)sz_drv + get_fattime(); /* Random seed */
                align = 0x100000 / (UINT)ss; /* Partition alignment for GPT [sector] */
                sz_ptbl = 128 * 128 / (UINT)ss; /* Size of partition table [sector] */
                top_bpt = sz_drv - sz_ptbl - 1; /* Backup partition table start LBA */
                nxt_alloc = 2 + sz_ptbl; /* First allocatable LBA */
                sz_pool = top_bpt - nxt_alloc; /* Size of allocatable area [sector] */
                bcc = 0xFFFFFFFF; sz_part = 1;
                pi = si = 0; /* partition table index, map index */
                do
                {
                    if (pi * 128 % ss == 0) memset(buf, 0, ss); /* Clean the buffer if needed */
                    if (sz_part != 0)
                    { /* Is the size table not termintated? */
                        nxt_alloc = (nxt_alloc + align - 1) & ((QWORD)0 - align); /* Align partition start LBA */
                        sz_part = plst[si++]; /* Get a partition size */
                        if (sz_part <= 100)
                        { /* Is the size in percentage? */
                            sz_part = sz_pool * sz_part / 100; /* Sectors in percentage */
                            sz_part = (sz_part + align - 1) & ((QWORD)0 - align); /* Align partition end LBA (only if in percentage) */
                        }
                        if (nxt_alloc + sz_part > top_bpt)
                        { /* Clip the size at end of the pool */
                            sz_part = (nxt_alloc < top_bpt) ? top_bpt - nxt_alloc : 0;
                        }
                    }
                    if (sz_part != 0)
                    { /* Add a partition? */
                        ofs = pi * 128 % ss;
                        memcpy(buf + ofs + 0, GUID_MS_Basic, 16); /* Set partition GUID (Microsoft Basic Data) */
                        rnd = make_rand(rnd, buf + ofs + 16, 16); /* Set unique partition GUID */
                        st_qword(buf + ofs + 32, nxt_alloc); /* Set partition start LBA */
                        st_qword(buf + ofs + 40, nxt_alloc + sz_part - 1); /* Set partition end LBA */
                        nxt_alloc += sz_part; /* Next allocatable LBA */
                    }
                    if ((pi + 1) * 128 % ss == 0)
                    { /* Write the sector buffer if it is filled up */
                        for (i = 0; i < ss; bcc = crc32(bcc, buf[i++])) ; /* Calculate table check sum */
                        if (disk_write(drv, buf, 2 + pi * 128 / ss, 1) != RES_OK) return FR_DISK_ERR; /* Write to primary table */
                        if (disk_write(drv, buf, top_bpt + pi * 128 / ss, 1) != RES_OK) return FR_DISK_ERR; /* Write to secondary table */
                    }
                } while (++pi < 128);
                /* Create primary GPT header */
                memset(buf, 0, ss);
                memcpy(buf + 0, [.. "EFI PART"u8, 0x0, 0x0, 0x1, 0x0, 0x5C, 0x0, 0x0], 16); /* Signature, version (1.0) and size (92) */
                st_dword(buf + 88, ~bcc); /* Table check sum */
                st_qword(buf + 24, 1); /* LBA of this header */
                st_qword(buf + 32, sz_drv - 1); /* LBA of secondary header */
                st_qword(buf + 40, 2 + sz_ptbl); /* LBA of first allocatable sector */
                st_qword(buf + 48, top_bpt - 1); /* LBA of last allocatable sector */
                st_dword(buf + 84, 128); /* Size of a table entry */
                st_dword(buf + 80, 128); /* Number of table entries */
                st_dword(buf + 72, 2); /* LBA of this table */
                rnd = make_rand(rnd, buf + 56, 16); /* Disk GUID */
                for (i = 0, bcc = 0xFFFFFFFF; i < 92; bcc = crc32(bcc, buf[i++])) ; /* Calculate header check sum */
                st_dword(buf + 16, ~bcc); /* Header check sum */
                if (disk_write(drv, buf, 1, 1) != RES_OK) return FR_DISK_ERR;
                /* Create secondary GPT header */
                st_qword(buf + 24, sz_drv - 1); /* LBA of this header */
                st_qword(buf + 32, 1); /* LBA of primary header */
                st_qword(buf + 72, top_bpt); /* LBA of this table */
                st_dword(buf + 16, 0);
                for (i = 0, bcc = 0xFFFFFFFF; i < 92; bcc = crc32(bcc, buf[i++])) ; /* Calculate header check sum */
                st_dword(buf + 16, ~bcc); /* Header check sum */
                if (disk_write(drv, buf, sz_drv - 1, 1) != RES_OK) return FR_DISK_ERR;
                /* Create protective MBR */
                memset(buf, 0, ss);
                memcpy(buf + 446, gpt_mbr, 16); /* Create a GPT partition */
                st_word(buf + 510, 0xAA55);
                if (disk_write(drv, buf, 0, 1) != RES_OK) return FR_DISK_ERR;
            }
            else
            { /* Create partitions in MBR format */
                sz_drv32 = (DWORD)sz_drv;
                n_sc = 63; /* Determine drive CHS without any consideration of the drive geometry */
                for (n_hd = 8; n_hd != 0 && sz_drv32 / n_hd / n_sc > 1024; n_hd *= 2) ;
                if (n_hd == 0) n_hd = 255; /* Number of heads needs to be <256 */
                memset(buf, 0, 4096); /* Clear MBR */
                pte = buf + 446; /* Partition table in the MBR */
                for (i = 0, nxt_alloc32 = n_sc; i < 4 && nxt_alloc32 != 0 && nxt_alloc32 < sz_drv32; i++, nxt_alloc32 += sz_part32)
                {
                    sz_part32 = (DWORD)plst[i]; /* Get partition size */
                    if (sz_part32 <= 100) sz_part32 = (sz_part32 == 100) ? sz_drv32 : sz_drv32 / 100 * sz_part32; /* Size in percentage? */
                    if (nxt_alloc32 + sz_part32 > sz_drv32 || nxt_alloc32 + sz_part32 < nxt_alloc32) sz_part32 = sz_drv32 - nxt_alloc32; /* Clip at drive size */
                    if (sz_part32 == 0) break; /* End of table or no sector to allocate? */
                    st_dword(pte + 8, nxt_alloc32); /* Partition start LBA sector */
                    st_dword(pte + 12, sz_part32); /* Size of partition [sector] */
                    pte[4] = sys; /* System type */
                    cy = (UINT)(nxt_alloc32 / n_sc / n_hd); /* Partitio start CHS cylinder */
                    hd = (BYTE)(nxt_alloc32 / n_sc % n_hd); /* Partition start CHS head */
                    sc = (BYTE)(nxt_alloc32 % n_sc + 1); /* Partition start CHS sector */
                    pte[1] = hd;
                    pte[2] = (BYTE)((cy >> 2 & 0xC0) | sc);
                    pte[3] = (BYTE)cy;
                    cy = (UINT)((nxt_alloc32 + sz_part32 - 1) / n_sc / n_hd); /* Partition end CHS cylinder */
                    hd = (BYTE)((nxt_alloc32 + sz_part32 - 1) / n_sc % n_hd); /* Partition end CHS head */
                    sc = (BYTE)((nxt_alloc32 + sz_part32 - 1) % n_sc + 1); /* Partition end CHS sector */
                    pte[5] = hd;
                    pte[6] = (BYTE)((cy >> 2 & 0xC0) | sc);
                    pte[7] = (BYTE)cy;
                    pte += 16; /* Next entry */
                }
                st_word(buf + 510, 0xAA55); /* MBR signature */
                if (disk_write(drv, buf, 0, 1) != RES_OK) return FR_DISK_ERR; /* Write it to the MBR */
            }
            return FR_OK;
        }
        public FRESULT f_mkfs(
         ReadOnlySpan<WCHAR> pathSpan, /* Logical drive number */
         MKFS_PARM? opt /* Format options */
        )
        {
            ReadOnlyPtr<WCHAR> path = pathSpan;
            WORD[] cst = [1, 4, 16, 64, 256, 512, 0]; /* Cluster size boundary for FAT volume (4K sector unit) */
            WORD[] cst32 = [1, 2, 4, 8, 16, 32, 0]; /* Cluster size boundary for FAT32 volume (128K sector unit) */
            MKFS_PARM defopt = new MKFS_PARM { fmt = FORMAT_TYPE.FM_ANY, n_fat = 0, align = 0, n_root = 0, au_size = 0 }; /* Default parameter */
            BYTE fsopt, fsty, sys, pdrv, ipart;
            Ptr<BYTE> buf;
            Ptr<BYTE> pte;
            WORD ss = 0; /* Sector size */
            DWORD sz_buf, sz_blk, n_clst, pau, nsect, n, vsn;
            LBA_t sz_vol, b_vol, b_fat, b_data; /* Volume size, base LBA of volume, base LBA of FAT and base LBA of data */
            LBA_t sect;
            Span<LBA_t> lbaSpan = stackalloc LBA_t[2];
            Ptr<LBA_t> lba = lbaSpan;
            DWORD sz_rsv, sz_fat, sz_dir, sz_au; /* Size of reserved area, FAT area, directry area, data area and cluster */
            UINT n_fat, n_root, i; /* Number of FATs, number of roor directory entries and some index */
            int vol;
            DSTATUS ds;
            FRESULT res;
            /* Check mounted drive and clear work area */
            vol = get_ldnumber(ref path); /* Get target logical drive */
            if (vol < 0) return FR_INVALID_DRIVE;
            if (vol >= FatFs.Length)
                Array.Resize(ref FatFs, vol + 1);
            if (FatFs[vol] != null)
                FatFs[vol] = null; /* Clear the fs object if mounted */
            pdrv = (BYTE)(vol); /* Hosting physical drive */
            ipart = 0; /* Hosting partition (0:create as new, 1..:existing partition) */
            /* Initialize the hosting physical drive */
            ds = (DSTATUS)disk_initialize(pdrv);
            if ((ds & 0x01) != 0) return FR_NOT_READY;
            if ((ds & 0x04) != 0) return FR_WRITE_PROTECTED;
            /* Get physical drive parameters (sz_drv, sz_blk and ss) */
            if (opt == null) opt = defopt; /* Use default parameter if it is not given */
            sz_blk = opt.align;
            if (sz_blk == 0) disk_ioctl(pdrv, 3, MemoryMarshal.AsBytes(new Span<DWORD>(ref sz_blk))); /* Block size from the parameter or lower layer */
            if (sz_blk == 0 || sz_blk > 0x8000 || (sz_blk & (sz_blk - 1)) != 0) sz_blk = 1; /* Use default if the block size is invalid */
            if (disk_ioctl(pdrv, 2, MemoryMarshal.AsBytes(new Span<WORD>(ref ss))) != RES_OK) return FR_DISK_ERR;
            if (ss > 4096 || ss < 512 || (ss & (ss - 1)) != 0) return FR_DISK_ERR;
            /* Options for FAT sub-type and FAT parameters */
            fsopt = (BYTE)((BYTE)opt.fmt & (0x07 | 0x08));
            n_fat = (UINT)((opt.n_fat >= 1 && opt.n_fat <= 2) ? opt.n_fat : 1);
            n_root = (opt.n_root >= 1 && opt.n_root <= 32768 && (opt.n_root % (ss / 32)) == 0) ? opt.n_root : 512;
            sz_au = (opt.au_size <= 0x1000000 && (opt.au_size & (opt.au_size - 1)) == 0) ? opt.au_size : 0;
            sz_au /= ss; /* Byte -. Sector */
            /* Get working buffer */
            sz_buf = 84000U /* LOH */ / ss; /* Size of working buffer [sector] */
            if (sz_buf == 0) return FR_NOT_ENOUGH_CORE;
            buf = new BYTE[sz_buf * ss]; /* Use heap memory for working buffer */
            /* Determine where the volume to be located (b_vol, sz_vol) */
            b_vol = sz_vol = 0;
            if (true && ipart != 0)
            { /* Is the volume associated with any specific partition? */
                /* Get partition location from the existing partition table */
                if (disk_read(pdrv, buf, 0, 1) != RES_OK) { return FR_DISK_ERR; }; /* Load MBR */
                if (ld_word(buf + 510) != 0xAA55) { return FR_MKFS_ABORTED; }; /* Check if MBR is valid */
                if (buf[446 + 4] == 0xEE)
                { /* GPT protective MBR? */
                    DWORD n_ent, ofs;
                    QWORD pt_lba;
                    /* Get the partition location from GPT */
                    if (disk_read(pdrv, buf, 1, 1) != RES_OK) { return FR_DISK_ERR; }; /* Load GPT header sector (next to MBR) */
                    if (test_gpt_header(buf) == 0) { return FR_MKFS_ABORTED; }; /* Check if GPT header is valid */
                    n_ent = ld_dword(buf + 80); /* Number of entries */
                    pt_lba = ld_qword(buf + 72); /* Table start sector */
                    ofs = i = 0;
                    while (n_ent != 0)
                    { /* Find MS Basic partition with order of ipart */
                        if (ofs == 0 && disk_read(pdrv, buf, pt_lba++, 1) != RES_OK) { return FR_DISK_ERR; }; /* Get PT sector */
                        if (!memcmp(buf + ofs + 0, GUID_MS_Basic, 16) && ++i == ipart)
                        { /* MS basic data partition? */
                            b_vol = ld_qword(buf + ofs + 32);
                            sz_vol = ld_qword(buf + ofs + 40) - b_vol + 1;
                            break;
                        }
                        n_ent--; ofs = (ofs + 128) % ss; /* Next entry */
                    }
                    if (n_ent == 0) { return FR_MKFS_ABORTED; }; /* Partition not found */
                    fsopt |= 0x80; /* Partitioning is in GPT */
                }
                else
                { /* Get the partition location from MBR partition table */
                    pte = buf + (446 + (ipart - 1) * 16);
                    if (ipart > 4 || pte[4] == 0) { return FR_MKFS_ABORTED; }; /* No partition? */
                    b_vol = ld_dword(pte + 8); /* Get volume start sector */
                    sz_vol = ld_dword(pte + 12); /* Get volume size */
                }
            }
            else
            { /* The volume is associated with a physical drive */
                if (disk_ioctl(pdrv, 1, MemoryMarshal.AsBytes(new Span<QWORD>(ref sz_vol))) != RES_OK) { return FR_DISK_ERR; };
                if ((fsopt & 0x08) == 0)
                { /* To be partitioned? */
                    /* Create a single-partition on the drive in this function */
                    if (sz_vol >= 0x10000000)
                    { /* Which partition type to create, MBR or GPT? */
                        fsopt |= 0x80; /* Partitioning is in GPT */
                        b_vol = 0x100000 / (UINT)ss; sz_vol -= b_vol + 128 * 128 / (UINT)ss + 1; /* Estimated partition offset and size */
                    }
                    else
                    { /* Partitioning is in MBR */
                        if (sz_vol > 63)
                        {
                            b_vol = 63; sz_vol -= b_vol; /* Estimated partition offset and size */
                        }
                    }
                }
            }
            if (sz_vol < 128) { return FR_MKFS_ABORTED; }; /* Check if volume size is >=128 sectors */
            /* Now start to create an FAT volume at b_vol and sz_vol */
            do
            { /* Pre-determine the FAT type */
                if (true && (fsopt & 0x04) != 0)
                { /* exFAT possible? */
                    if ((fsopt & 0x07) == 0x04 || sz_vol >= 0x4000000 || sz_au > 128)
                    { /* exFAT only, vol >= 64M sectors or sz_au > 128 sectors ? */
                        fsty = 4; break;
                    }
                }
                if (sz_vol >= 0x100000000) { return FR_MKFS_ABORTED; }; /* Too large volume for FAT/FAT32 */
                if (sz_au > 128) sz_au = 128; /* Invalid AU for FAT/FAT32? */
                if ((fsopt & 0x02) != 0)
                { /* FAT32 possible? */
                    if ((fsopt & 0x01) == 0)
                    { /* no-FAT? */
                        fsty = 3; break;
                    }
                }
                if ((fsopt & 0x01) == 0) { return FR_INVALID_PARAMETER; }; /* no-FAT? */
                fsty = 2;
            } while (false);
            vsn = (DWORD)sz_vol + get_fattime(); /* VSN generated from current time and partition size */
            if (fsty == 4)
            { /* Create an exFAT volume */
                DWORD szb_bit, szb_case, sum, nbit, clu;
                Span<DWORD> clenSpan = stackalloc DWORD[3];
                Ptr<DWORD> clen = clenSpan;
                WCHAR ch, si;
                UINT j, st;
                if (sz_vol < 0x1000) { return FR_MKFS_ABORTED; }; /* Too small volume for exFAT? */
                /* Determine FAT location, data location and number of clusters */
                if (sz_au == 0)
                { /* AU auto-selection */
                    sz_au = 8;
                    if (sz_vol >= 0x80000) sz_au = 64; /* >= 512Ks */
                    if (sz_vol >= 0x4000000) sz_au = 256; /* >= 64Ms */
                }
                b_fat = b_vol + 32; /* FAT start at offset 32 */
                sz_fat = (DWORD)((sz_vol / sz_au + 2) * 4 + ss - 1) / ss; /* Number of FAT sectors */
                b_data = (b_fat + sz_fat + sz_blk - 1) & ~((LBA_t)sz_blk - 1); /* Align data area to the erase block boundary */
                if (b_data - b_vol >= sz_vol / 2) { return FR_MKFS_ABORTED; }; /* Too small volume? */
                n_clst = (DWORD)((sz_vol - (b_data - b_vol)) / sz_au); /* Number of clusters */
                if (n_clst < 16) { return FR_MKFS_ABORTED; }; /* Too few clusters? */
                if (n_clst > 0x7FFFFFFD) { return FR_MKFS_ABORTED; }; /* Too many clusters? */
                szb_bit = (n_clst + 7) / 8; /* Size of allocation bitmap */
                clen[0] = (szb_bit + sz_au * ss - 1) / (sz_au * ss); /* Number of allocation bitmap clusters */
                /* Create a compressed up-case table */
                sect = b_data + sz_au * clen[0]; /* Table start sector */
                sum = 0; /* Table checksum to be stored in the 82 entry */
                st = 0; si = (WCHAR)0; i = 0; j = 0; szb_case = 0;
                do
                {
                    switch (st)
                    {
                        case 0:
                            ch = (WCHAR)char.ToUpper((char)si); /* Get an up-case char */
                            if (ch != si)
                            {
                                si++; break; /* Store the up-case char if exist */
                            }
                            for (j = 1; (WCHAR)(si + j) != 0 && (WCHAR)(si + j) == (WCHAR)char.ToUpper((char)(si + j)); j++) ; /* Get run length of no-case block */
                            if (j >= 128)
                            {
                                ch = (WCHAR)0xFFFF; st = 2; break; /* Compress the no-case block if run is >= 128 chars */
                            }
                            st = 1; /* Do not compress short run */
                            ch = si++; /* Fill the short run */
                            if (--j == 0) st = 0;
                            break;
                        case 1:
                            ch = si++; /* Fill the short run */
                            if (--j == 0) st = 0;
                            break;
                        default:
                            ch = (WCHAR)j; si += (WCHAR)j; /* Number of chars to skip */
                            st = 0;
                            break;
                    }
                    sum = xsum32(buf[i + 0] = (BYTE)ch, sum); /* Put it into the write buffer */
                    sum = xsum32(buf[i + 1] = (BYTE)(ch >> 8), sum);
                    i += 2; szb_case += 2;
                    if (si == 0 || i == sz_buf * ss)
                    { /* Write buffered data when buffer full or end of process */
                        n = (i + ss - 1) / ss;
                        if (disk_write(pdrv, buf, sect, n) != RES_OK) { return FR_DISK_ERR; };
                        sect += n; i = 0;
                    }
                } while (si != 0);
                clen[1] = (szb_case + sz_au * ss - 1) / (sz_au * ss); /* Number of up-case table clusters */
                clen[2] = 1; /* Number of root directory clusters */
                /* Initialize the allocation bitmap */
                sect = b_data; nsect = (szb_bit + ss - 1) / ss; /* Start of bitmap and number of bitmap sectors */
                nbit = clen[0] + clen[1] + clen[2]; /* Number of clusters in-use by system (bitmap, up-case and root-dir) */
                do
                {
                    memset(buf, 0, sz_buf * ss); /* Initialize bitmap buffer */
                    for (i = 0; nbit != 0 && i / 8 < sz_buf * ss; buf[i / 8] |= (BYTE)(1 << ((int)i % 8)), i++, nbit--) ; /* Mark used clusters */
                    n = (nsect > sz_buf) ? sz_buf : nsect; /* Write the buffered data */
                    if (disk_write(pdrv, buf, sect, n) != RES_OK) { return FR_DISK_ERR; };
                    sect += n; nsect -= n;
                } while (nsect != 0);
                /* Initialize the FAT */
                sect = b_fat; nsect = sz_fat; /* Start of FAT and number of FAT sectors */
                j = nbit = clu = 0;
                do
                {
                    memset(buf, 0, sz_buf * ss); i = 0; /* Clear work area and reset write offset */
                    if (clu == 0)
                    { /* Initialize FAT [0] and FAT[1] */
                        st_dword(buf + i, 0xFFFFFFF8); i += 4; clu++;
                        st_dword(buf + i, 0xFFFFFFFF); i += 4; clu++;
                    }
                    do
                    { /* Create chains of bitmap, up-case and root directory */
                        while (nbit != 0 && i < sz_buf * ss)
                        { /* Create a chain */
                            st_dword(buf + i, (nbit > 1) ? clu + 1 : 0xFFFFFFFF);
                            i += 4; clu++; nbit--;
                        }
                        if (nbit == 0 && j < 3) nbit = clen[j++]; /* Get next chain length */
                    } while (nbit != 0 && i < sz_buf * ss);
                    n = (nsect > sz_buf) ? sz_buf : nsect; /* Write the buffered data */
                    if (disk_write(pdrv, buf, sect, n) != RES_OK) { return FR_DISK_ERR; };
                    sect += n; nsect -= n;
                } while (nsect != 0);
                /* Initialize the root directory */
                memset(buf, 0, sz_buf * ss);
                buf[32 * 0 + 0] = 0x83; /* Volume label entry (no label) */
                buf[32 * 1 + 0] = 0x81; /* Bitmap entry */
                st_dword(buf + 32 * 1 + 20, 2); /*  cluster */
                st_dword(buf + 32 * 1 + 24, szb_bit); /*  size */
                buf[32 * 2 + 0] = 0x82; /* Up-case table entry */
                st_dword(buf + 32 * 2 + 4, sum); /*  sum */
                st_dword(buf + 32 * 2 + 20, 2 + clen[0]); /*  cluster */
                st_dword(buf + 32 * 2 + 24, szb_case); /*  size */
                sect = b_data + sz_au * (clen[0] + clen[1]); nsect = sz_au; /* Start of the root directory and number of sectors */
                do
                { /* Fill root directory sectors */
                    n = (nsect > sz_buf) ? sz_buf : nsect;
                    if (disk_write(pdrv, buf, sect, n) != RES_OK) { return FR_DISK_ERR; };
                    memset(buf, 0, ss); /* Rest of entries are filled with zero */
                    sect += n; nsect -= n;
                } while (nsect != 0);
                /* Create two set of the exFAT VBR blocks */
                sect = b_vol;
                for (n = 0; n < 2; n++)
                {
                    /* Main record (+0) */
                    memset(buf, 0, ss);
                    memcpy(buf + 0, [0xEB, 0x76, 0x90, .. "EXFAT   "u8], 11); /* Boot jump code (x86), OEM name */
                    st_qword(buf + 64, b_vol); /* Volume offset in the physical drive [sector] */
                    st_qword(buf + 72, sz_vol); /* Volume size [sector] */
                    st_dword(buf + 80, (DWORD)(b_fat - b_vol)); /* FAT offset [sector] */
                    st_dword(buf + 84, sz_fat); /* FAT size [sector] */
                    st_dword(buf + 88, (DWORD)(b_data - b_vol)); /* Data offset [sector] */
                    st_dword(buf + 92, n_clst); /* Number of clusters */
                    st_dword(buf + 96, 2 + clen[0] + clen[1]); /* Root directory cluster number */
                    st_dword(buf + 100, vsn); /* VSN */
                    st_word(buf + 104, 0x100); /* Filesystem version (1.00) */
                    for (buf[108] = 0, i = ss; (i >>= 1) != 0; buf[108]++) ; /* Log2 of sector size [byte] */
                    for (buf[109] = 0, i = sz_au; (i >>= 1) != 0; buf[109]++) ; /* Log2 of cluster size [sector] */
                    buf[110] = 1; /* Number of FATs */
                    buf[111] = 0x80; /* Drive number (for int13) */
                    st_word(buf + 120, 0xFEEB); /* Boot code (x86) */
                    st_word(buf + 510, 0xAA55); /* Signature (placed here regardless of sector size) */
                    for (i = sum = 0; i < ss; i++)
                    { /* VBR checksum */
                        if (i != 106 && i != 106 + 1 && i != 112) sum = xsum32(buf[i], sum);
                    }
                    if (disk_write(pdrv, buf, sect++, 1) != RES_OK) { return FR_DISK_ERR; };
                    /* Extended bootstrap record (+1..+8) */
                    memset(buf, 0, ss);
                    st_word(buf + (ss - 2), 0xAA55); /* Signature (placed at end of sector) */
                    for (j = 1; j < 9; j++)
                    {
                        for (i = 0; i < ss; sum = xsum32(buf[i++], sum)) ; /* VBR checksum */
                        if (disk_write(pdrv, buf, sect++, 1) != RES_OK) { return FR_DISK_ERR; };
                    }
                    /* OEM/Reserved record (+9..+10) */
                    memset(buf, 0, ss);
                    for (; j < 11; j++)
                    {
                        for (i = 0; i < ss; sum = xsum32(buf[i++], sum)) ; /* VBR checksum */
                        if (disk_write(pdrv, buf, sect++, 1) != RES_OK) { return FR_DISK_ERR; };
                    }
                    /* Sum record (+11) */
                    for (i = 0; i < ss; i += 4) st_dword(buf + i, sum); /* Fill with checksum value */
                    if (disk_write(pdrv, buf, sect++, 1) != RES_OK) { return FR_DISK_ERR; };
                }
            }
            else
            { /* Create an FAT/FAT32 volume */
                do
                {
                    pau = sz_au;
                    /* Pre-determine number of clusters and FAT sub-type */
                    if (fsty == 3)
                    { /* FAT32 volume */
                        if (pau == 0)
                        { /* AU auto-selection */
                            n = (DWORD)sz_vol / 0x20000; /* Volume size in unit of 128KS */
                            for (i = 0, pau = 1; cst32[i] != 0 && cst32[i] <= n; i++, pau <<= 1) ; /* Get from table */
                        }
                        n_clst = (DWORD)sz_vol / pau; /* Number of clusters */
                        sz_fat = (n_clst * 4 + 8 + ss - 1) / ss; /* FAT size [sector] */
                        sz_rsv = 32; /* Number of reserved sectors */
                        sz_dir = 0; /* No static directory */
                        if (n_clst <= 0xFFF5 || n_clst > 0x0FFFFFF5) { return FR_MKFS_ABORTED; };
                    }
                    else
                    { /* FAT volume */
                        if (pau == 0)
                        { /* au auto-selection */
                            n = (DWORD)sz_vol / 0x1000; /* Volume size in unit of 4KS */
                            for (i = 0, pau = 1; cst[i] != 0 && cst[i] <= n; i++, pau <<= 1) ; /* Get from table */
                        }
                        n_clst = (DWORD)sz_vol / pau;
                        if (n_clst > 0xFF5)
                        {
                            n = n_clst * 2 + 4; /* FAT size [byte] */
                        }
                        else
                        {
                            fsty = 1;
                            n = (n_clst * 3 + 1) / 2 + 3; /* FAT size [byte] */
                        }
                        sz_fat = (n + ss - 1) / ss; /* FAT size [sector] */
                        sz_rsv = 1; /* Number of reserved sectors */
                        sz_dir = (DWORD)n_root * 32 / ss; /* Root directory size [sector] */
                    }
                    b_fat = b_vol + sz_rsv; /* FAT base */
                    b_data = b_fat + sz_fat * n_fat + sz_dir; /* Data base */
                    /* Align data area to erase block boundary (for flash memory media) */
                    n = (DWORD)(((b_data + sz_blk - 1) & ~(sz_blk - 1)) - b_data); /* Sectors to next nearest from current data base */
                    if (fsty == 3)
                    { /* FAT32: Move FAT */
                        sz_rsv += n; b_fat += n;
                    }
                    else
                    { /* FAT: Expand FAT */
                        if ((n % n_fat) != 0)
                        { /* Adjust fractional error if needed */
                            n--; sz_rsv++; b_fat++;
                        }
                        sz_fat += n / n_fat;
                    }
                    /* Determine number of clusters and final check of validity of the FAT sub-type */
                    if (sz_vol < b_data + pau * 16 - b_vol) { return FR_MKFS_ABORTED; }; /* Too small volume? */
                    n_clst = ((DWORD)sz_vol - sz_rsv - sz_fat * n_fat - sz_dir) / pau;
                    if (fsty == 3)
                    {
                        if (n_clst <= 0xFFF5)
                        { /* Too few clusters for FAT32? */
                            if (sz_au == 0 && (sz_au = pau / 2) != 0) continue; /* Adjust cluster size and retry */
                            { return FR_MKFS_ABORTED; };
                        }
                    }
                    if (fsty == 2)
                    {
                        if (n_clst > 0xFFF5)
                        { /* Too many clusters for FAT16 */
                            if (sz_au == 0 && (pau * 2) <= 64)
                            {
                                sz_au = pau * 2; continue; /* Adjust cluster size and retry */
                            }
                            if ((fsopt & 0x02) != 0)
                            {
                                fsty = 3; continue; /* Switch type to FAT32 and retry */
                            }
                            if (sz_au == 0 && (sz_au = pau * 2) <= 128) continue; /* Adjust cluster size and retry */
                            { return FR_MKFS_ABORTED; };
                        }
                        if (n_clst <= 0xFF5)
                        { /* Too few clusters for FAT16 */
                            if (sz_au == 0 && (sz_au = pau * 2) <= 128) continue; /* Adjust cluster size and retry */
                            { return FR_MKFS_ABORTED; };
                        }
                    }
                    if (fsty == 1 && n_clst > 0xFF5) { return FR_MKFS_ABORTED; }; /* Too many clusters for FAT12 */
                    /* Ok, it is the valid cluster configuration */
                    break;
                } while (true);
                /* Create FAT VBR */
                memset(buf, 0, ss);
                memcpy(buf + 0, [0xEB, 0xFE, 0x90, .. "MSDOS5.0"u8], 11); /* Boot jump code (x86), OEM name */
                st_word(buf + 11, ss); /* Sector size [byte] */
                buf[13] = (BYTE)pau; /* Cluster size [sector] */
                st_word(buf + 14, (WORD)sz_rsv); /* Size of reserved area */
                buf[16] = (BYTE)n_fat; /* Number of FATs */
                st_word(buf + 17, (WORD)((fsty == 3) ? 0 : n_root)); /* Number of root directory entries */
                if (sz_vol < 0x10000)
                {
                    st_word(buf + 19, (WORD)sz_vol); /* Volume size in 16-bit LBA */
                }
                else
                {
                    st_dword(buf + 32, (DWORD)sz_vol); /* Volume size in 32-bit LBA */
                }
                buf[21] = 0xF8; /* Media descriptor byte */
                st_word(buf + 24, 63); /* Number of sectors per track (for int13) */
                st_word(buf + 26, 255); /* Number of heads (for int13) */
                st_dword(buf + 28, (DWORD)b_vol); /* Volume offset in the physical drive [sector] */
                if (fsty == 3)
                {
                    st_dword(buf + 67, vsn); /* VSN */
                    st_dword(buf + 36, sz_fat); /* FAT size [sector] */
                    st_dword(buf + 44, 2); /* Root directory cluster # (2) */
                    st_word(buf + 48, 1); /* Offset of FSINFO sector (VBR + 1) */
                    st_word(buf + 50, 6); /* Offset of backup VBR (VBR + 6) */
                    buf[64] = 0x80; /* Drive number (for int13) */
                    buf[66] = 0x29; /* Extended boot signature */
                    memcpy(buf + 71, "NO NAME    FAT32   "u8, 19); /* Volume label, FAT signature */
                }
                else
                {
                    st_dword(buf + 39, vsn); /* VSN */
                    st_word(buf + 22, (WORD)sz_fat); /* FAT size [sector] */
                    buf[36] = 0x80; /* Drive number (for int13) */
                    buf[38] = 0x29; /* Extended boot signature */
                    memcpy(buf + 43, "NO NAME    FAT     "u8, 19); /* Volume label, FAT signature */
                }
                st_word(buf + 510, 0xAA55); /* Signature (offset is fixed here regardless of sector size) */
                if (disk_write(pdrv, buf, b_vol, 1) != RES_OK) { return FR_DISK_ERR; }; /* Write it to the VBR sector */
                /* Create FSINFO record if needed */
                if (fsty == 3)
                {
                    disk_write(pdrv, buf, b_vol + 6, 1); /* Write backup VBR (VBR + 6) */
                    memset(buf, 0, ss);
                    st_dword(buf + 0, 0x41615252);
                    st_dword(buf + 484, 0x61417272);
                    st_dword(buf + 488, n_clst - 1); /* Number of free clusters */
                    st_dword(buf + 492, 2); /* Last allocated cluster# */
                    st_word(buf + 510, 0xAA55);
                    disk_write(pdrv, buf, b_vol + 7, 1); /* Write backup FSINFO (VBR + 7) */
                    disk_write(pdrv, buf, b_vol + 1, 1); /* Write original FSINFO (VBR + 1) */
                }
                /* Initialize FAT area */
                memset(buf, 0, sz_buf * ss);
                sect = b_fat; /* FAT start sector */
                for (i = 0; i < n_fat; i++)
                { /* Initialize FATs each */
                    if (fsty == 3)
                    {
                        st_dword(buf + 0, 0xFFFFFFF8); /* FAT[0] */
                        st_dword(buf + 4, 0xFFFFFFFF); /* FAT[1] */
                        st_dword(buf + 8, 0x0FFFFFFF); /* FAT[2] (root directory at cluster# 2) */
                    }
                    else
                    {
                        st_dword(buf + 0, (fsty == 1) ? 0xFFFFF8 : 0xFFFFFFF8); /* FAT[0] and FAT[1] */
                    }
                    nsect = sz_fat; /* Number of FAT sectors */
                    do
                    { /* Fill FAT sectors */
                        n = (nsect > sz_buf) ? sz_buf : nsect;
                        if (disk_write(pdrv, buf, sect, (UINT)n) != RES_OK) { return FR_DISK_ERR; };
                        memset(buf, 0, ss); /* Rest of FAT area is initially zero */
                        sect += n; nsect -= n;
                    } while (nsect != 0);
                }
                /* Initialize root directory (fill with zero) */
                nsect = (fsty == 3) ? pau : sz_dir; /* Number of root directory sectors */
                do
                {
                    n = (nsect > sz_buf) ? sz_buf : nsect;
                    if (disk_write(pdrv, buf, sect, (UINT)n) != RES_OK) { return FR_DISK_ERR; };
                    sect += n; nsect -= n;
                } while (nsect != 0);
            }
            /* A FAT volume has been created here */
            /* Determine system ID in the MBR partition table */
            if (true && fsty == 4)
            {
                sys = 0x07; /* exFAT */
            }
            else if (fsty == 3)
            {
                sys = 0x0C; /* FAT32X */
            }
            else if (sz_vol >= 0x10000)
            {
                sys = 0x06; /* FAT12/16 (large) */
            }
            else if (fsty == 2)
            {
                sys = 0x04; /* FAT16 */
            }
            else
            {
                sys = 0x01; /* FAT12 */
            }
            /* Update partition information */
            if (true && ipart != 0)
            { /* Volume is in the existing partition */
                if (false || (fsopt & 0x80) == 0)
                { /* Is the partition in MBR? */
                    /* Update system ID in the partition table */
                    if (disk_read(pdrv, buf, 0, 1) != RES_OK) { return FR_DISK_ERR; }; /* Read the MBR */
                    buf[446 + (ipart - 1) * 16 + 4] = sys; /* Set system ID */
                    if (disk_write(pdrv, buf, 0, 1) != RES_OK) { return FR_DISK_ERR; }; /* Write it back to the MBR */
                }
            }
            else
            { /* Volume as a new single partition */
                if ((fsopt & 0x08) == 0)
                { /* Create partition table if not in SFD format */
                    lba[0] = sz_vol; lba[1] = 0;
                    res = create_partition(pdrv, lba, sys, buf);
                    if (res != FR_OK) { return res; };
                }
            }
            if (disk_ioctl(pdrv, 0, default) != RES_OK) { return FR_DISK_ERR; };
            { return FR_OK; };
        }
        /*-----------------------------------------------------------------------*/
        /* Create Partition Table on the Physical Drive                          */
        /*-----------------------------------------------------------------------*/
        public FRESULT f_fdisk(
         BYTE pdrv, /* Physical drive number */
    LBA_t[] ptbl /* Pointer to the size table for each partitions */
)
        {
            DSTATUS stat;
            FRESULT res;
            /* Initialize the physical drive */
            stat = (DSTATUS)disk_initialize(pdrv);
            if ((stat & 0x01) != 0) return FR_NOT_READY;
            if ((stat & 0x04) != 0) return FR_WRITE_PROTECTED;
            Ptr<byte> buf = new byte[4096]; /* Use heap memory for working buffer */
            res = create_partition(pdrv, ptbl, 0x07, buf); /* Create partitions (system ID is temporary setting and determined by f_mkfs) */
            return res;
        }

        public FRESULT CheckResult(FRESULT result, ReadOnlySpan<FRESULT> allowedStates = default, [CallerArgumentExpression("result")] string expr = "")
        {
            foreach (var allowed in allowedStates)
                if (result == allowed)
                    return result;
            if (result != FRESULT.FR_OK)
                throw new IOException($"FAT operation '{expr}' failed with result: {result}");
            return result;
        }

        public void Write(
            FIL fp, /* Open file to be written */
            Stream stream, /* Data to be written */
            int bufferSize = 84000
        )
        {
            var buf = new byte[bufferSize];
            while (true)
            {
                var read = stream.Read(buf);
                if (read == 0)
                    break;
                var res = f_write(fp, buf.AsSpan(0, read), out var written);
                if (res != FRESULT.FR_OK)
                    throw new IOException($"Failed to wrute to FAT file with result: {res}");
                if (written != read)
                    throw new IOException($"Failed to wrute to FAT file. Requested = {read}, Written = {written}");
            }
        }

        public void Write(
            FIL fp, /* Open file to be written */
            Span<byte> data /* Data to be written */
        )
        {
            var res = f_write(fp, data, out var written);
            if (res != FRESULT.FR_OK)
                throw new IOException($"Failed to wrute to FAT file with result: {res}");
            if (written != data.Length)
                throw new IOException($"Failed to wrute to FAT file. Requested = {data.Length}, Written = {written}");
        }

        public void CreateFileSafe(ILogger logger, string filename, Stream stream, bool doThrow)
        {
            FIL? file = null;
            try
            {
                CheckResult(f_open(out file, filename, MODE.FA_CREATE_NEW | MODE.FA_WRITE));
                Write(file, stream);
                try
                {
                    CheckResult(f_close(file));
                }
                finally
                {
                    file = null;
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, $"Failed to write file {filename}, removing files to prevent corruption");
                if (file != null)
                    f_close(file); // ignore failure
                f_unlink(filename); // try to cleanup to prevent load of invalid file, ignore failure
                if (doThrow)
                    throw;
            }
        }
    }
}
