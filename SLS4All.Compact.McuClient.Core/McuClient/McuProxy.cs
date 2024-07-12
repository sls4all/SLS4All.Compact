// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.IO;
using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Net.Sockets;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace SLS4All.Compact.McuClient
{
    public sealed class McuProxy
    {
        public const int DefaultPort = 5001;
        private readonly ILogger _logger;

        public McuProxy(ILogger<McuProxy> logger)
        {
            _logger = logger;
        }

        public async Task Run(int? port)
        {
            if (port == null)
                port = DefaultPort;
            _logger.LogInformation($"MCU proxy will listen on port {port}");
            try
            {
                var listener = new TcpListener(new IPEndPoint(IPAddress.Any, port.Value));
                listener.Start();
                while (true)
                {
                    var client = await listener.AcceptTcpClientAsync();
                    _ = Task.Run(() => RunClient(client));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unhandled exception: {ex}");
            }
        }

        private async Task RunClient(TcpClient client)
        {
            string remote = "";
            try
            {
                using (client)
                using (var stream = client.GetStream())
                {
                    remote = client.Client.RemoteEndPoint!.ToString()!;
                    _logger.LogInformation($"Got new client from {remote}");
                    client.ReceiveBufferSize = 1024 * 1022;
                    client.SendBufferSize = 1024 * 1022;
                    client.NoDelay = true;

                    string port;
                    int baud;
                    using (var reader = new BinaryReader(stream, Encoding.UTF8, true))
                    {
                        var command = reader.ReadString();
                        if (command.Length == 0)
                        {
                            _logger.LogDebug($"Responding with port names to {remote}");
                            using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
                            {
                                string[] portNames;
                                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                                {
                                    portNames = SerialPort.GetPortNames();
                                }
                                else
                                {
                                    portNames = Directory.GetFiles("/dev/serial/by-id").Concat(SerialPort.GetPortNames()).ToArray();
                                }
                                writer.Write(portNames.Length);
                                foreach (var portName in portNames)
                                {
                                    writer.Write(portName);
                                }
                            }
                            return;
                        }
                        else
                        {
                            port = command;
                            baud = reader.ReadInt32();
                        }
                    }

                    string realPort;
                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                        realPort = port;
                    else
                    {
                        var linkTarget = new FileInfo(port).LinkTarget;
                        if (linkTarget != null)
                            realPort = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(port) ?? "/", linkTarget));
                        else
                            realPort = port;
                    }
                    _logger.LogInformation($"Opening {port} ({realPort}) at baud {baud} for {remote}");
                    using (var serialPort = new SerialPortEx(_logger, realPort, baud))
                    {
                        var cancelSource = new CancellationTokenSource();
                        var cancel = cancelSource.Token;
                        var readTask = Task.CompletedTask;
                        var writeTask = Task.CompletedTask;
                        try
                        {
                            readTask = Task.Run(async () =>
                            {
                                try
                                {
                                    var buffer = new byte[16384];
                                    while (true)
                                    {
                                        var read = await serialPort.BaseStream.ReadAsync(buffer, cancel);
                                        if (read == 0)
                                            break;
                                        await stream.WriteAsync(buffer.AsMemory(0, read), cancel);
                                        await stream.FlushAsync(cancel);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (!cancel.IsCancellationRequested)
                                        _logger.LogError($"Exception in read task for {remote}: {ex}");
                                }
                            });
                            writeTask = Task.Run(async () =>
                            {
                                try
                                {
                                    var buffer = new byte[16384];
                                    while (true)
                                    {
                                        var read = await stream.ReadAsync(buffer, cancel);
                                        if (read == 0)
                                            break;
                                        await serialPort.BaseStream.WriteAsync(buffer.AsMemory(0, read), cancel);
                                        await serialPort.BaseStream.FlushAsync(cancel);
                                    }
                                }
                                catch (Exception ex)
                                {
                                    if (!cancel.IsCancellationRequested)
                                        _logger.LogError($"Exception in write task for {remote}: {ex}");
                                }
                            });
                            await Task.WhenAny(readTask, writeTask);
                        }
                        finally
                        {
                            cancelSource.Cancel();
                        }
                        await Task.WhenAll(readTask, writeTask);
                    }
                }
                _logger.LogDebug($"Finished {remote}");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Unhandled exception for {remote}: {ex}");
            }
        }
    }
}
