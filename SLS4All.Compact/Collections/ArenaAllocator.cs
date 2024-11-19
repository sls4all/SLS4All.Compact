// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Collections
{
    public sealed class ArenaAllocator<T>
    {
        private readonly Lock _lock = new();
        private readonly bool _needsClear;
        private readonly int _arenaLength;
        private volatile Arena<T> _currentArena;
        private readonly ConcurrentStack<Arena<T>> _freeArenas;
        private readonly ConcurrentStack<Arena<T>> _allArenas;

        public static int BestArenaLength { get; } = 131072 / Unsafe.SizeOf<T>(); // should be larger than LOH threshold (85000) to ensure the buffers are not part of compacting!
        public int ArenaLength => _arenaLength;
        public int FreeArenas => _freeArenas.Count;
        public int CreatedArenas => _allArenas.Count;
        public int UsedArenas => _allArenas.Count - _freeArenas.Count;

        public ArenaAllocator(int arenaLength)
        {
            _arenaLength = arenaLength;
            _needsClear = RuntimeHelpers.IsReferenceOrContainsReferences<T>();
            _allArenas = new ConcurrentStack<Arena<T>>();
            _freeArenas = new ConcurrentStack<Arena<T>>();
            _currentArena = CreateArena();
        }

        public void Clear()
        {
            _freeArenas.Clear();
            foreach (var arena in _allArenas)
                _freeArenas.Push(arena);
            _currentArena = CreateArena();
        }

        private Arena<T> CreateArena()
        {
            if (_freeArenas.TryPop(out var arena))
            {
                arena.ResetInternal();
                return arena;
            }
            else
            {
                arena = new Arena<T>(this, new T[_arenaLength]);
                _allArenas.Push(arena);
                return arena;
            }
        }

        public ArenaBuffer<T> Allocate(int length)
        {
            if (length == 0)
                return default;
            while (true)
            {
                var arena = _currentArena;
                if (arena.TryAllocate(length, out var buffer))
                    return buffer;
                var newArena = CreateArena();
                lock (_lock)
                {
                    if (Interlocked.CompareExchange(ref _currentArena, newArena, arena) == arena)
                    {
                        if (arena.IsFree)
                        {
                            if (_needsClear)
                                Array.Clear(arena.TotalBuffer);
                            _freeArenas.Push(arena);
                        }
                    }
                    else
                        _freeArenas.Push(newArena);
                }
            }
        }

        internal void AddReference(in ArenaBuffer<T> buffer)
            => buffer.Arena?.AddReferenceInternal(buffer);

        internal void DecrementReference(in ArenaBuffer<T> buffer)
        {
            var arena = buffer.Arena;
            if (arena?.DecrementReferenceInternal(buffer) == true)
            {
                var newArena = CreateArena();
                if (_needsClear)
                    Array.Clear(arena.TotalBuffer);
                lock (_lock)
                {
                    if (Interlocked.CompareExchange(ref _currentArena, newArena, arena) == arena)
                    {
                        _freeArenas.Push(arena);
                    }
                    else
                    {
                        _freeArenas.Push(newArena);
                        _freeArenas.Push(arena);
                    }
                }
            }
        }

        public int GetReferencedItemCount()
        {
            var count = 0;
            foreach (var arena in _allArenas)
                count += arena.ReferenceCount;
            return count;
        }
    }
}
