// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using Renci.SshNet;
using Renci.SshNet.Common;
using Renci.SshNet.Messages;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.IO;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.IsolatedStorage;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Devices
{
    public class McuSshSerialDeviceFactoryOptions
    {
        public string Name { get; set; } = "local";
        public bool UseIoctlForBaud { get; set; } = true;
        public string CCompiler { get; set; } = "gcc";
        public string? Host { get; set; }
        public int Port { get; set; } = 22;
        public string? UserName { get; set; }
        public string? Password { get; set; }
        public byte[]? ExpectedFingerprint { get; set; }
        public string? PrivateKeyFilename { get; set; }
        public string? PrivateKeyPassword { get; set; }
        public string IoctlOutBinary { get; set; } = "/tmp/sls4all_set_baud";
    }

    public sealed class McuSshSerialDeviceFactory : IMcuDeviceFactory
    {
        private sealed class McuSshSerialDevice : McuDeviceBase, IMcuDevice
        {
            private readonly McuSshSerialDeviceFactory _factory;
            private SshClient _client;
            private SshPipedShell _fromSerialShell;
            private SshPipedShell _toSerialShell;
            private readonly McuDeviceInfo _info;

            public override IMcuDeviceFactory Factory => _factory;
            public override McuDeviceInfo Info => _info;

            public McuSshSerialDevice(McuSshSerialDeviceFactory factory, SshClient client, SshPipedShell fromSerialShell, SshPipedShell toSerialShell, McuDeviceInfo info)
            {
                _factory = factory;
                _client = client;
                _fromSerialShell = fromSerialShell;
                _toSerialShell = toSerialShell;
                _info = info;
            }

            public override ValueTask<int> Read(Memory<byte> buffer, CancellationToken cancel = default)
            {
                if (_fromSerialShell == null)
                    throw new InvalidOperationException("Device not open");
                return _fromSerialShell.FromShellStream.ReadAsync(buffer, cancel);
            }

            public override ValueTask Write(ReadOnlyMemory<byte> buffer, CancellationToken cancel = default)
            {
                if (_toSerialShell == null)
                    throw new InvalidOperationException("Device not open");
                return _toSerialShell.ToShellStream.WriteAsync(buffer, cancel);
            }

            public override Task Flush(CancellationToken cancel = default)
            {
                if (_toSerialShell == null)
                    throw new InvalidOperationException("Device not open");
                return _toSerialShell.ToShellStream.FlushAsync(cancel);
            }

            public override void Dispose()
            {
                _toSerialShell.Dispose();
                _fromSerialShell.Dispose();
                _client.Dispose();
            }
        }

        private readonly IOptionsMonitor<McuSshSerialDeviceFactoryOptions> _options;
        private readonly IOptionsMonitor<McuAliasesOptions> _aliasOptions;
        private readonly static object _ioctlBaudSync = new();

        public string FactoryName => _options.CurrentValue.Name;

        public McuSshSerialDeviceFactory(
            IOptionsMonitor<McuSshSerialDeviceFactoryOptions> options,
            IOptionsMonitor<McuAliasesOptions> aliasOptions)
        {
            _options = options;
            _aliasOptions = aliasOptions;
        }

        public async ValueTask<McuDeviceInfo[]> GetDeviceNames(CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var aliasOptions = _aliasOptions.CurrentValue;
            var deviceNames = Array.Empty<string>();
            var client = CreateClient();
            using (cancel.Register(client.Dispose))
            {
                await Task.Run(() =>
                {
                    client.Connect();
                    var res = client.RunCommand($"ls /dev/serial/by-id/*").Result;
                    deviceNames = res.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries);
                });
            }
            return aliasOptions.GetMatches(deviceNames);
        }

        public async Task<IMcuDevice> Open(McuDeviceInfo info, CancellationToken cancel = default)
        {
            var options = _options.CurrentValue;
            var aliasOptions = _aliasOptions.CurrentValue;
            var client = CreateClient();
            McuSshSerialDevice serial = null!;
            using (cancel.Register(client.Dispose))
            {
                await Task.Run(async () =>
                {
                    client.Connect();
                    var exists = client.RunCommand($"[ -e {info.Endpoint} ]");
                    if (exists.ExitStatus != 0)
                        throw new FileNotFoundException($"Device {info.Endpoint} was not found");

                    var stty = client.RunCommand($"stty -F {info.Endpoint} ignbrk -brkint -icrnl -imaxbel -opost onlcr -onocr -onlret -isig -icanon -iexten -echo -echoe -echok echoctl echoke -iutf8 -istrip -ixon -ixoff crtscts time 0 min 1 {(!options.UseIoctlForBaud ? info.Baud.ToString() : "")}");
                    if (stty.ExitStatus != 0)
                        throw new IOException($"Setting serial port failed with code {stty.ExitStatus}: {stty.Error}");
                    if (options.UseIoctlForBaud)
                        SetBaudIoctl(client, info.Endpoint, info.Baud);

                    var fromSerialShell = new SshPipedShell(client);
                    var toSerialShell = new SshPipedShell(client);

                    await fromSerialShell.DiscardToThisPoint(cancel);
                    await toSerialShell.DiscardToThisPoint(cancel);

                    await fromSerialShell.WriteLine($"[ -e {info.Endpoint} ] && cat {info.Endpoint}", cancel);
                    await toSerialShell.WriteLine($"[ -e {info.Endpoint} ] && cat >{info.Endpoint}", cancel);

                    serial = new McuSshSerialDevice(this, client, fromSerialShell, toSerialShell, info);
                });
            }
            return serial;
        }

        private void SetBaudIoctl(SshClient client, string deviceName, int baud)
        {
            lock (_ioctlBaudSync)
            {
                var options = _options.CurrentValue;
                var code = $@"#include <stdio.h>
#include <fcntl.h>
#include <errno.h>
#include <asm/termios.h>

int main()
{{
    int fd, ret;
    struct termios2 config;

    fd = open(""{deviceName}"", O_RDWR);
    if (fd < 0)
        return 1;

    ret = ioctl(fd, TCGETS2, &config);
    if (!ret) {{
        config.c_cflag &= ~CBAUD;
        config.c_cflag |= BOTHER;
        config.c_ospeed = {baud};

        config.c_cflag &= ~(CBAUD << IBSHIFT);
        config.c_cflag |= BOTHER << IBSHIFT;
        config.c_ispeed = {baud};

        config.c_oflag |= CRTSCTS;
        ret = ioctl(fd, TCSETS2, &config);
    }}
    close(fd);
    return ret;
}}".Replace("\r", "").Replace("\n", "\\n");
                var compile = client.RunCommand($"printf '{code}' | {options.CCompiler} -o {options.IoctlOutBinary} -xc -");
                if (compile.ExitStatus != 0)
                    throw new IOException($"Failed to compile ioctl code with exit code {compile.ExitStatus}: {compile.Error}");
                var run = client.RunCommand(options.IoctlOutBinary);
                if (compile.ExitStatus != 0)
                    throw new IOException($"Failed to run ioctl code with exit code {run.ExitStatus}: {run.Error}");
            }
        }

        private SshClient CreateClient()
        {
            var options = _options.CurrentValue;
            if (string.IsNullOrWhiteSpace(options.Host))
                throw new InvalidOperationException("SSH host was not specified");
            if (string.IsNullOrWhiteSpace(options.UserName))
                throw new InvalidOperationException("SSH username was not specified");
            var methods = new List<AuthenticationMethod>();
            if (options.Password != null)
                methods.Add(new PasswordAuthenticationMethod(options.UserName, options.Password));
            if (!string.IsNullOrWhiteSpace(options.PrivateKeyFilename))
            {
                if (!string.IsNullOrWhiteSpace(options.PrivateKeyPassword))
                    methods.Add(new PrivateKeyAuthenticationMethod(options.UserName, new PrivateKeyFile(options.PrivateKeyFilename, options.PrivateKeyPassword)));
                else
                    methods.Add(new PrivateKeyAuthenticationMethod(options.UserName, new PrivateKeyFile(options.PrivateKeyFilename)));
            }
            var connectionInfo = new ConnectionInfo(
                options.Host,
                options.Port,
                options.UserName,
                methods.ToArray());
            var client = new SshClient(connectionInfo);
            client.HostKeyReceived += (sender, e) =>
            {
                if (options.ExpectedFingerprint == null)
                    e.CanTrust = true;
                else if (options.ExpectedFingerprint.Length == e.FingerPrint.Length)
                    e.CanTrust = options.ExpectedFingerprint.SequenceEqual(e.FingerPrint);
                else
                    e.CanTrust = false;
            };
            return client;
        }
    }
}
