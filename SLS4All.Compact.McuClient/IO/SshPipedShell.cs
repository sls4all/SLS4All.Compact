// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Renci.SshNet.Common;
using Renci.SshNet;
using System;
using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace SLS4All.Compact.IO
{
    public sealed class SshPipedShell : IDisposable
    {
        private const int _shellBufferSize = 65536;
        private readonly Shell _shell;
        private readonly Pipe _toShellPipe;
        private readonly Pipe _fromShellPipe;
        private readonly Stream _fromShellStream;
        private readonly Stream _toShellStream;

        public Pipe ToShellPipe => _toShellPipe;
        public Pipe FromShellPipe => _fromShellPipe;
        public Stream ToShellStream => _toShellStream;
        public Stream FromShellStream => _fromShellStream;

        public SshPipedShell(SshClient client)
        {
            _toShellPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
            _fromShellPipe = new Pipe(new PipeOptions(useSynchronizationContext: false));
            _toShellStream = _toShellPipe.Writer.AsStream();
            _fromShellStream = _fromShellPipe.Reader.AsStream();
            var modes = new Dictionary<TerminalModes, uint>
                {
                    { TerminalModes.ECHO, 0 },
                    { TerminalModes.ECHOE, 0 },
                    { TerminalModes.ECHOK, 0 },
                    { TerminalModes.ECHOCTL, 0 },
                    { TerminalModes.ECHOKE, 0 },
                    { TerminalModes.OPOST, 0 },
                    { TerminalModes.ICRNL, 0 },
                    { TerminalModes.IMAXBEL, 0 },
                    { TerminalModes.ONLCR, 0 },
                    { TerminalModes.OCRNL, 0 },
                    { TerminalModes.ONOCR, 0 },
                    { TerminalModes.ONLRET, 0 },
                    { TerminalModes.ISIG, 0 },
                    { TerminalModes.ICANON, 0 },
                    { TerminalModes.IUTF8, 0 },
                    { TerminalModes.ISTRIP, 0 },
                    { TerminalModes.IEXTEN, 0 },
                    { TerminalModes.IXON, 0 },
                    { TerminalModes.IXOFF, 0 },
                };
            _shell = client.CreateShell(_toShellPipe.Reader.AsStream(), _fromShellPipe.Writer.AsStream(), null, "", 0, 0, 0, 0, modes, _shellBufferSize);
            _shell.Start();
        }

        public async ValueTask DiscardToThisPoint(CancellationToken cancel)
        {
            var id = Guid.NewGuid().ToString();
            await WriteLine($"echo {id}", cancel);
            await WriteLine($"echo {id}", cancel);
            var str = "";
            var bytes = new byte[1024];
            int foundFirstIndex = -1;
            while (true)
            {
                var read = await _fromShellStream.ReadAsync(bytes, cancel);
                if (read == 0)
                    break;
                str += Encoding.ASCII.GetString(bytes, 0, read);
                if (foundFirstIndex == -1)
                    foundFirstIndex = str.IndexOf(id);
                if (foundFirstIndex != -1)
                {
                    var secondIndex = str.IndexOf(id, foundFirstIndex + id.Length);
                    if (secondIndex != -1)
                    {
                        var intermezzo = str.Substring(foundFirstIndex, secondIndex - foundFirstIndex);
                        if (str.IndexOf(intermezzo, secondIndex) != -1)
                            break;
                    }
                }
            }
        }

        public async ValueTask WriteLine(string text, CancellationToken cancel)
        {
            await _toShellPipe.Writer.WriteAsync(Encoding.ASCII.GetBytes(text + "\n"), cancel);
            await _toShellPipe.Writer.FlushAsync(cancel);
        }

        public void Dispose()
        {
            _toShellPipe.Writer.Complete();
            _fromShellPipe.Writer.Complete();
            _shell.Stop();
            _shell.Dispose();
        }
    }
}
