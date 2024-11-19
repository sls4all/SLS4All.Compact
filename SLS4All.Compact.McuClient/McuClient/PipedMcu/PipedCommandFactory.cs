// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    public sealed class PipedCommandFactory
    {
        private readonly Lock _locker = new();
        private readonly ArenaAllocator<byte> _arena;
        private readonly IMcu _mcu;
        private FrozenDictionary<int, ConcurrentBag<McuCommand>> _bags = FrozenDictionary<int, ConcurrentBag<McuCommand>>.Empty;
        private long s_createdCommandCount;

        public long CreatedCommands => Interlocked.Read(ref s_createdCommandCount);
        public int CreatedArenas => _arena.CreatedArenas;

        public PipedCommandFactory(IMcu mcu)
        {
            _arena = new ArenaAllocator<byte>(ArenaAllocator<byte>.BestArenaLength);
            _mcu = mcu;
        }

        private void UpdateBagsInner()
        {
            _bags = _mcu.Config.IdToCommand.ToDictionary(x => x.Key, x => 
            {
                if (_bags.TryGetValue(x.Key, out var existing))
                    return existing;
                else
                    return new ConcurrentBag<McuCommand>();
            }).ToFrozenDictionary();
        }

        private ConcurrentBag<McuCommand> GetBag(int id)
        {
            if (!_bags.TryGetValue(id, out var bag))
            {
                lock (_locker)
                {
                    if (!_bags.TryGetValue(id, out bag))
                        UpdateBagsInner();
                }
                bag = _bags[id];
            }
            return bag;
        }

        public ArenaBuffer<byte> BorrowBuffer(int length)
            => _arena.Allocate(length);

        public McuCommand BorrowCommand(int id)
        {
            var bag = GetBag(id);
            if (!bag.TryTake(out var command))
            {
                Interlocked.Increment(ref s_createdCommandCount);
                command = _mcu.Config.IdToCommand[id].Clone();
            }
            return command;
        }

        public void ReturnCommand(McuCommand command)
        {
            for (int i = 0; i < command.ArgumentCount; i++)
            {
                var arg = command[i];
                if (arg.ArenaBuffer.Arena != null)
                {
                    arg.ArenaBuffer.DecrementReference();
                    command[i] = default;
                }
            }
            var bag = GetBag(command.CommandId);
            bag.Add(command);
        }
    }
}
