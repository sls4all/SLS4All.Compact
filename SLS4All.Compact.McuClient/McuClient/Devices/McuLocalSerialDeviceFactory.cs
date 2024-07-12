// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Renci.SshNet;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;
using SLS4All.Compact.McuClient.Pins.Tmc2208;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
using System.IO.Ports;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace SLS4All.Compact.McuClient.Devices
{
    public class McuLocalSerialDeviceFactoryOptions
    {
        public string Name { get; set; } = "local";
    }

    public class McuLocalSerialDeviceFactory : IMcuDeviceFactory
    {
        private sealed class LocalDevice : McuDeviceBase, IMcuDevice
        {
            private readonly McuLocalSerialDeviceFactory _factory;
            private readonly SerialPortEx _serial;
            private readonly Stream _stream;
            private readonly McuDeviceInfo _info;

            public override IMcuDeviceFactory Factory => _factory;
            public override McuDeviceInfo Info => _info;

            public LocalDevice(McuLocalSerialDeviceFactory factory, SerialPortEx serial, Stream stream, McuDeviceInfo info)
            {
                _factory = factory;
                _serial = serial;
                _stream = stream;
                _info = info;
            }

            public override ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancel = default)
                => _stream.ReadAsync(buffer, cancel);

            public override ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
                => _stream.WriteAsync(buffer, cancel);

            public override Task Flush(CancellationToken cancel = default)
                => _stream.FlushAsync(cancel);

            public override void Dispose()
            {
                _stream.Dispose();
                _serial.Dispose();
            }
        }

        private readonly ILogger _logger;
        private readonly IOptionsMonitor<McuLocalSerialDeviceFactoryOptions> _options;
        private readonly IOptionsMonitor<McuAliasesOptions> _aliasOptions;

        public string FactoryName => _options.CurrentValue.Name;

        public McuLocalSerialDeviceFactory(
            ILogger<McuLocalSerialDeviceFactory> logger,
            IOptionsMonitor<McuLocalSerialDeviceFactoryOptions> options,
            IOptionsMonitor<McuAliasesOptions> aliasOptions)
        {
            _logger = logger;
            _options = options;
            _aliasOptions = aliasOptions;
        }

        public ValueTask<McuDeviceInfo[]> GetDeviceNames(CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var aliasOptions = _aliasOptions.CurrentValue;
            var deviceNames = Directory.GetFiles("/dev/serial/by-id").Concat(SerialPort.GetPortNames()).ToArray();
            return new ValueTask<McuDeviceInfo[]>(aliasOptions.GetMatches(deviceNames));
        }

        public Task<IMcuDevice> Open(McuDeviceInfo info, CancellationToken cancel = default)
        {
            cancel.ThrowIfCancellationRequested();
            var serial = new SerialPortEx(_logger, info.Endpoint, info.Baud);
            return Task.FromResult<IMcuDevice>(new LocalDevice(this, serial, serial.BaseStream, info));
        }
    }
}
