// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using SLS4All.Compact.Threading;
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public sealed class McuConfigCommands
    {
        private readonly Lock _lock = new();
        private readonly List<McuCommand> _initCommands = new();
        private readonly List<McuCommand> _configCommands = new();
        private readonly List<McuCommand> _restartCommands = new();
        private volatile IMcu? _initializedMcu;
        private volatile int _nextOid;

        public IReadOnlyList<McuCommand> InitCommands => _initCommands;
        public IReadOnlyList<McuCommand> ConfigCommands => _configCommands;
        public IReadOnlyList<McuCommand> RestartCommands => _restartCommands;

        public AsyncEvent<McuConfigCommands> InitializingEvent { get; } = new(ordered: true);

        private void CheckAlreadyInitialized()
        {
            if (_initializedMcu != null)
                throw new InvalidOperationException($"Already initialized MCU {_initializedMcu.Name}");
        }

        public void Add(McuCommand command, bool onInit = false, bool onRestart = false)
        {
            CheckAlreadyInitialized();
            lock (_lock)
            {
                AddInner(command, onInit, onRestart);
            }
        }

        private void AddInner(McuCommand command, bool onInit = false, bool onRestart = false)
        {
            if (onInit)
                _initCommands.Add(command);
            else if (onRestart)
                _restartCommands.Add(command);
            else
                _configCommands.Add(command);
        }

        public async Task InitializeOnly(ILogger? logger, IMcu mcu, CancellationToken cancel)
        {
            logger?.LogInformation($"Initializing MCU {mcu.Name}");
            Debug.Assert(_initializedMcu == null || _initializedMcu == mcu);
            _nextOid = 0;
            _initializedMcu = null;
            _initCommands.Clear();
            _restartCommands.Clear();
            _configCommands.Clear();
            await InitializingEvent.Invoke(this, cancel);
            _initializedMcu = mcu;
        }

        public async Task<bool> TryInitializeAndSend(ILogger? logger, IMcu mcu, int priority, int? prevCrc, int? requestedMoveCount, CancellationToken cancel)
        {
            McuCommand[] config, restart, init;
            int crc;

            await InitializeOnly(logger, mcu, cancel);

            lock (_lock)
            {
                _configCommands.Insert(0, mcu.LookupCommand("allocate_oids count=%c").Bind(_nextOid));

                var commands = _configCommands.Concat(_restartCommands).Concat(_initCommands).ToArray();
                var commandsStr = string.Join(
                    "\n", 
                    commands.Select(x => x.ToString())
                        .Append($"requestedMoveCount={requestedMoveCount}"));
                crc = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(SHA1.HashData(MemoryMarshal.AsBytes(commandsStr.AsSpan()))));
                if (prevCrc != null)
                {
                    if (prevCrc != crc)
                    {
                        logger?.LogWarning($"MCU {mcu.Name} config CRC does not match. Needs {crc} and got {prevCrc}.");
                        return false;
                    }
                }
                else
                {
                    if (requestedMoveCount != null && mcu.TryLookupCommand("finalize_config_with_moves crc=%u move_count=%u", out var finalize))
                        AddInner(finalize.Bind(crc, requestedMoveCount.Value));
                    else
                        AddInner(mcu.LookupCommand("finalize_config crc=%u").Bind(crc));
                }
                config = _configCommands.ToArray();
                restart = _restartCommands.ToArray();
                init = _initCommands.ToArray();
            }
            if (prevCrc == null)
            {
                logger?.LogInformation($"Sending MCU {mcu.Name} printer configuration (1/2: config)");
                foreach (var command in config)
                    mcu.Send(command, priority, McuOccasion.Now);
            }
            else
            {
                logger?.LogInformation($"Sending MCU {mcu.Name} printer configuration (1/2: restart)");
                foreach (var command in restart)
                    mcu.Send(command, priority, McuOccasion.Now);
            }
            logger?.LogInformation($"Sending MCU {mcu.Name} printer configuration (2/2: init)");
            foreach (var command in init)
                mcu.Send(command, priority, McuOccasion.Now);
            return true;
        }

        public int CreateOid()
            => Interlocked.Increment(ref _nextOid) - 1;
    }
}
