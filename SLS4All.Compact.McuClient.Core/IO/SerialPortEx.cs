// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.IO.Pipes;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public sealed class SerialPortEx : IDisposable
    {
        private const uint TCGETS2 = unchecked((uint)-2144578518);
        private const uint CBAUD = 4111;
        private const uint BOTHER = 4096;
        private const uint TCSETS2 = unchecked((uint)1076646955);
        private const uint CRTSCTS = unchecked((uint)-2147483648);
        private const uint NCCS = 19;
        private const int VTIME = 5;
        private const int VMIN = 6;
        private const int IBSHIFT = 16;

        [StructLayout(LayoutKind.Sequential)]
        private unsafe struct termios2
        {
            public uint c_iflag;       /* input mode flags */
            public uint c_oflag;       /* output mode flags */
            public uint c_cflag;       /* control mode flags */
            public uint c_lflag;       /* local mode flags */
            public byte c_line;            /* line discipline */
            public fixed byte c_cc[19];        /* control characters */
            public uint c_ispeed;       /* input speed */
            public uint c_ospeed;       /* output speed */
        };


        private readonly System.IO.Ports.SerialPort _serialPort;
        private readonly Stream _stream;

        public Stream BaseStream => _stream;

        public SerialPortEx(ILogger logger, string path, int baud)
        {
            try
            {
                _serialPort = new SerialPort(path, 460800 /* some initial supported baud */);
                _serialPort.Open();
                _stream = _serialPort.BaseStream;

                // force arbitrary baud from argument
                SetCorrectBaud(logger, _serialPort, baud);
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        private unsafe void SetCorrectBaud(ILogger logger, SerialPort serialPort, int baud)
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                var stream = _serialPort.BaseStream;
                var streamType = stream.GetType();
                var handleField = streamType.GetField("_handle", BindingFlags.NonPublic | BindingFlags.Instance);
                if (handleField != null)
                {
                    var handleObj = (SafeHandle)handleField.GetValue(stream)!;
                    var handle = (int)handleObj.DangerousGetHandle();
                    var termios2 = new termios2();
                    var res = ioctl(handle, TCGETS2, ref termios2);
                    if (res != 0)
                        throw new IOException("Failed to execute ioctl TCGETS2");
                    termios2.c_cflag &= ~CBAUD;
                    termios2.c_cflag |= BOTHER;
                    termios2.c_ospeed = (uint)baud;

                    termios2.c_cflag &= ~(CBAUD << IBSHIFT);
                    termios2.c_cflag |= BOTHER << IBSHIFT;
                    termios2.c_ispeed = (uint)baud;

                    termios2.c_oflag |= CRTSCTS;
                    res = ioctl(handle, TCSETS2, ref termios2);
                    if (res != 0)
                        throw new IOException("Failed to execute ioctl TCSETS2");
                }
                else
                    logger.LogWarning("Failed to find system handle for serial port to set the correct baud");
            }
        }

        [DllImport("libc")]
        private extern static int ioctl(int fd, uint code, ref termios2 data);

        public void Dispose()
        {
            _stream?.Dispose();
            _serialPort?.Dispose();
        }
    }
}
