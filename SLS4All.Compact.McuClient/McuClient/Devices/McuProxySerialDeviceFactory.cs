// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipelines;
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
    public class McuProxySerialDeviceFactoryOptions
    {
        public string Name { get; set; } = "local";
        public string? Host { get; set; }
        public int Port { get; set; } = 5001;
    }

    public class McuProxySerialDeviceFactory : IMcuDeviceFactory
    {
        private sealed class ProxyDevice : McuDeviceBase, IMcuDevice
        {
            private readonly McuProxySerialDeviceFactory _factory;
            private readonly TcpClient _client;
            private readonly Stream _stream;
            private readonly McuDeviceInfo _info;

            public override IMcuDeviceFactory Factory => _factory;
            public override McuDeviceInfo Info => _info;

            public ProxyDevice(McuProxySerialDeviceFactory factory, TcpClient client, Stream stream, McuDeviceInfo info)
            {
                _factory = factory;
                _client = client;
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
                _client.Dispose();
            }
        }


        private readonly IOptionsMonitor<McuProxySerialDeviceFactoryOptions> _options;
        private readonly IOptionsMonitor<McuAliasesOptions> _aliasOptions;

        public string FactoryName => _options.CurrentValue.Name;

        public McuProxySerialDeviceFactory(
            IOptionsMonitor<McuProxySerialDeviceFactoryOptions> options,
            IOptionsMonitor<McuAliasesOptions> aliasOptions)
        {
            _options = options;
            _aliasOptions = aliasOptions;
        }

        public async ValueTask<McuDeviceInfo[]> GetDeviceNames(CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var aliasOptions = _aliasOptions.CurrentValue;
            var deviceNames = await Task.Run(() =>
            {
                using (var tcpClient = CreateClient())
                using (var stream = tcpClient.GetStream())
                using (var writer = new BinaryWriter(stream, Encoding.UTF8))
                using (var reader = new BinaryReader(stream, Encoding.UTF8))
                {
                    writer.Write("");
                    writer.Flush();
                    var count = reader.ReadInt32();
                    var deviceNames = new List<string>();
                    for (int i = 0; i < count; i++)
                    {
                        var name = reader.ReadString();
                        deviceNames.Add(name);
                    }
                    return deviceNames.ToArray();
                }
            });
            return aliasOptions.GetMatches(deviceNames);
        }

        public Task<IMcuDevice> Open(McuDeviceInfo info, CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var aliasOptions = _aliasOptions.CurrentValue;
            return Task.Run(() =>
            {
                var client = CreateClient();
                try
                {
                    var stream = client.GetStream();
                    using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                    using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                    {
                        writer.Write(info.Endpoint);
                        writer.Write(info.Baud);
                        writer.Flush();
                    }
                    return (IMcuDevice)new ProxyDevice(this, client, stream, info);
                }
                catch
                {
                    client.Dispose();
                    throw;
                }
            });
        }

        private TcpClient CreateClient()
        {
            var options = _options.CurrentValue;
            if (string.IsNullOrWhiteSpace(options.Host))
                throw new InvalidOperationException("Host was not specified");
            var client = new TcpClient(options.Host, options.Port);
            client.ReceiveBufferSize = 1024 * 1024;
            client.SendBufferSize = 1024 * 1024;
            client.NoDelay = true;
            return client;
        }
    }
}
