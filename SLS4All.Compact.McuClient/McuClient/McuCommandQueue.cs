// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿//#define COMMAND_QUEUE_TRACING // Uncomment for debug tracing of command queue operations

using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Threading;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    [Flags]
    public enum McuCommandQueueItemFlags
    {
        None = 0,
        Dequeued = 1,
        Retransmit = 2,
        TimingCritical = 4,
    }

    public delegate ValueTask McuResponseHandler(Exception? exception, McuCommand? command, CancellationToken cancel);

    public struct McuCommandQueueItem
    {
        public int Index;
        public int Priority;
        public ulong Id;
        public long MinClock;
        public long ReqClock;
        public int RefCount;
        public ArenaBuffer<byte> Data;
        public McuCommand CommandTemplate;
        public McuCommandQueueItemFlags Flags;
    }

    public sealed record class McuCommandQueueStats
    {
        public int SentCount { get; set; }
        public long SentBytes { get; set; }
        public int StalledCount { get; set; }
        public int AllocatedCount { get; set; }
        public int ReferencedArenaItems { get; set; }
        public int UsedArenaItemCount { get; set; }
        public int UsedArenaCount { get; set; }
        public long UsedArenaBytes { get; set; }
        public int CreatedArenaCount { get; set; }
        public long CreatedArenaBytes { get; set; }
        public long StalledBytes { get; set; }
        public int InflightCount { get; set; }
        public long InflightBytes { get; set; }
        public int TimingCriticalCount { get; set; }
    }

    public sealed class McuCommandQueue
    {
        private readonly record struct KeyReq(int Priority, long MinClock, long ReqClock, ulong Id) : IComparable<KeyReq>
        {
            public int CompareTo(KeyReq other)
            {
                if (this.ReqClock < other.ReqClock)
                    return -1;
                if (this.ReqClock > other.ReqClock)
                    return 1;
                if (this.Priority > other.Priority)
                    return -1;
                if (this.Priority < other.Priority)
                    return 1;
                if (this.MinClock < other.MinClock)
                    return -1;
                if (this.MinClock > other.MinClock)
                    return 1;
                if (this.Id < other.Id)
                    return -1;
                if (this.Id > other.Id)
                    return 1;
                return 0;
            }
        }

        private readonly record struct KeyMin(int Priority, long MinClock, ulong Id) : IComparable<KeyMin>
        {
            public int CompareTo(KeyMin other)
            {
                if (this.MinClock < other.MinClock)
                    return -1;
                if (this.MinClock > other.MinClock)
                    return 1;
                if (this.Priority > other.Priority)
                    return -1;
                if (this.Priority < other.Priority)
                    return 1;
                if (this.Id < other.Id)
                    return -1;
                if (this.Id > other.Id)
                    return 1;
                return 0;
            }
        }

        private const long _maxFutureReqClockDuration = int.MaxValue / 2;
        private readonly ArenaAllocator<byte> _arena;
        private readonly McuCodec _encoder;
        private readonly McuAbstract _mcu;

        private readonly PriorityQueue<int, KeyReq> _priorityReq;
        private readonly PriorityQueue<int, KeyMin> _priorityMin;
        private readonly Stack<int> _freeItems;
        private long _reorderCount;
        private AutoResetEvent _reorderEvent;
        private McuCommandQueueItem[] _items;
        private int _sentCount;
        private long _sentBytes;
        private int _stalledCount;
        private long _stalledBytes;
        private int _inflightCount;
        private long _inflightBytes;
        private int _timingCriticalCount;

        private readonly TaskQueue? _dumpQueue;
        private readonly TextWriter? _dumpWriter;

        public long ReorderCount => Interlocked.Read(ref _reorderCount);
        public WaitHandle ReorderEvent => _reorderEvent;
        public event EventHandler<ulong>? AckedIdEvent;
        public static bool IsTracing
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
#if COMMAND_QUEUE_TRACING
                return true;
#else
                return false;
#endif
            }
        }

        public int TimingCriticalCount => _timingCriticalCount;

        public ArenaAllocator<byte> Arena => _arena;

        public ref McuCommandQueueItem this[int index]
            => ref _items[index];

        private bool ShouldCloneCommands
        {
            get
            {
#if COMMAND_QUEUE_TRACING
                return true;
#else
                return _dumpWriter != null;
#endif
            }
        }

        public McuCommandQueue(McuAbstract mcu, McuOptions options)
        {
            _mcu = mcu;
            _arena = new ArenaAllocator<byte>(ArenaAllocator<byte>.BestArenaLength);
            _encoder = new();

            _reorderEvent = new AutoResetEvent(false);
            _priorityReq = new();
            _priorityMin = new();
            _freeItems = new();
            _items = new McuCommandQueueItem[1024];

            if (options.DumpCommandQueueToFile && options.DumpCommandQueueFileFormat != null)
            {
                var dumpFilename = string.Format(options.DumpCommandQueueFileFormat, DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss"));
                var dumpDirectory = Path.GetDirectoryName(dumpFilename);
                if (!string.IsNullOrEmpty(dumpDirectory))
                    Directory.CreateDirectory(dumpDirectory);
                _dumpQueue = new TaskQueue();
                _dumpWriter = new StreamWriter(File.Open(dumpFilename, FileMode.Create, FileAccess.Write, FileShare.Read));
            }

            Clear();
        }

        private ArenaBuffer<byte> CreateBuffer(int length)
            => _arena.Allocate(length);

        private void RefBuffer(in ArenaBuffer<byte> data)
            => data.AddReference();

        private void FreeBuffer(ref ArenaBuffer<byte> data)
        {
            data.DecrementReference();
            data = default;
        }

        private int GetFreeIndex()
        {
            if (_freeItems.TryPop(out var index))
            {
                Debug.Assert(_items[index].RefCount == 0);
                return index;
            }
            else
            {
                var oldSize = _items.Length;
                Array.Resize(ref _items, oldSize * 2);
                for (int i = _items.Length - 1; i >= oldSize; i--)
                    _freeItems.Push(i);
                return _freeItems.Pop();
            }
        }

        private Span<byte> Encode(McuCommand command)
        {
            _encoder.ResetWrite();
            _encoder.WriteVLQ(command.CommandId);
            for (int i = 0; i < command.ArgumentCount; i++)
            {
                var arg = command.Arguments[i];
                switch (arg.Type)
                {
                    case McuCommandArgumentType.Number:
                        _encoder.WriteVLQ(command[i].Int64);
                        break;
                    case McuCommandArgumentType.String:
                        {
                            var buffer = command[i].Buffer;
                            _encoder.WriteVLQ(buffer.Count);
                            if (!_encoder.TryWrite(buffer.AsSpan()))
                                throw new InvalidOperationException("Failed to write argument string");
                            break;
                        }
                    default:
                        throw new InvalidOperationException($"Invalid argument type: {arg.Type}");
                }
            }
            return _encoder.FinalizeCommand().Span;
        }

        public bool TryReplace(McuSendResult id, McuCommand command)
        {
            ref var item = ref _items[id.Index];
            if (item.Id == id.Id &&
                (item.Flags & McuCommandQueueItemFlags.Dequeued) == 0)
            {
                _stalledCount--;
                _stalledBytes -= item.Data.Length;
                Debug.Assert(item.RefCount > 0);

                var span = Encode(command);
                var data = CreateBuffer(span.Length);
                span.CopyTo(data.Span);

                FreeBuffer(ref item.Data);
                item.Data = data;
                item.CommandTemplate = ShouldCloneCommands ? command.Clone() : command;
                _stalledCount++;
                _stalledBytes += data.Length;
                return true;
            }
            else
                return false;
        }

        public McuSendResult Enqueue(McuCommand command, int priority, long minClock, long reqClock)
        {
#if DEBUG
            if (!_mcu.ClockSync.IsReady)
            {
                Debug.Assert(minClock == 0 && reqClock == 0);
            }
            else
            {
                var clockAt = _mcu.ClockSync.GetClock(SystemTimestamp.Now);
                Debug.Assert(reqClock == 0 || reqClock >= clockAt, "Requested clock is behind current time. This typically happens when the program execution is paused at breakpoint or stepped trough. That can't be done since the program requires precise timing.");
            }
#endif
            var span = Encode(command);
            var data = CreateBuffer(span.Length);
            span.CopyTo(data.Span);

            var id = ++_encoder.Seq; // NOTE: we are using encoder.Seq as Id generator, not a true Seq!
            var index = GetFreeIndex();
            ref var item = ref _items[index];
            item.Index = index;
            item.Priority = priority;
            item.Id = id;
            item.MinClock = minClock;
            item.ReqClock = reqClock;
            item.Data = data;
            item.CommandTemplate = ShouldCloneCommands ? command.Clone() : command;
            item.Flags = McuCommandQueueItemFlags.None;
            if (command.IsTimingCritical)
            {
                item.Flags |= McuCommandQueueItemFlags.TimingCritical;
                _timingCriticalCount++;
            }

            EnqueueToPriority(ref item);
            return new McuSendResult(id, index);
        }

        private void EnqueueToPriority(ref McuCommandQueueItem item)
        {
#if COMMAND_QUEUE_TRACING
            Trace.WriteLine($"ENQUEUE {_mcu.Alias}: {item.CommandTemplate} / {item.Index}");
#endif
            _stalledCount++;
            _stalledBytes += item.Data.Length;
            item.RefCount += 2;
            _priorityReq.Enqueue(item.Index, new KeyReq(Priority: item.Priority, MinClock: item.MinClock, ReqClock: item.ReqClock, Id: item.Id));
            _priorityMin.Enqueue(item.Index, new KeyMin(Priority: item.Priority, MinClock: item.MinClock, Id: item.Id));
            if (_priorityReq.Peek() == item.Index || _priorityMin.Peek() == item.Index)
            {
                Interlocked.Increment(ref _reorderCount);
                _reorderEvent.Set();
            }
        }

        private void FreeItem(ref McuCommandQueueItem item)
        {
            Debug.Assert(item.RefCount == 0);
            if ((item.Flags & McuCommandQueueItemFlags.TimingCritical) != 0)
            {
                Debug.Assert(_timingCriticalCount > 0);
                _timingCriticalCount--;
            }
            FreeBuffer(ref item.Data);
            _freeItems.Push(item.Index);
        }

        public void AckItem(int ackIndex)
        {
            ref var item = ref _items[ackIndex];
            if ((item.Flags & McuCommandQueueItemFlags.Dequeued) == 0)
            {
                item.Flags |= McuCommandQueueItemFlags.Dequeued;
                Debug.Assert((item.Flags & McuCommandQueueItemFlags.Retransmit) != 0);
            }
            else
            {
                _inflightCount--;
                _inflightBytes -= item.Data.Length;
            }
            AckedIdEvent?.Invoke(this, item.Id);
            Debug.Assert(item.RefCount > 0);
            if (--item.RefCount <= 0)
                FreeItem(ref item);
        }

        public void NakItem(int nakIndex)
        {
            ref var item = ref _items[nakIndex];
            if ((item.Flags & McuCommandQueueItemFlags.Dequeued) == 0) // not yet acually dequeued (still in queue)
                return;
#if COMMAND_QUEUE_TRACING
            Trace.WriteLine($"NAK {_mcu.Alias} id={item.Id}");
#endif
            item.Flags |= McuCommandQueueItemFlags.Retransmit;
            _inflightCount--;
            _inflightBytes -= item.Data.Length;
            if (--item.RefCount <= 0)
                FreeItem(ref item);
        }

        public void Remove(McuSendResult id)
        {
            ref var item = ref _items[id.Index];
            if (item.Id == id.Id && 
                (item.Flags & McuCommandQueueItemFlags.Dequeued) == 0)
            {
                item.Flags |= McuCommandQueueItemFlags.Dequeued;
                _stalledCount--;
                _stalledBytes -= item.Data.Length;
                Debug.Assert(item.RefCount > 0);
                // NOTE: do not add to free items, will be added when RefCount reaches zero
            }
        }

        private void DequeuedItem(ref McuCommandQueueItem item)
        {
            if ((item.Flags & McuCommandQueueItemFlags.Dequeued) == 0)
            {
#if COMMAND_QUEUE_TRACING
                Trace.WriteLine($"DEQUEUE {_mcu.Alias}: {item.CommandTemplate} / {item.Index}");
#endif
                item.Flags |= McuCommandQueueItemFlags.Dequeued;
                _sentCount++;
                _sentBytes += item.Data.Length;
                _stalledCount--;
                _stalledBytes -= item.Data.Length;
                _inflightCount++;
                _inflightBytes += item.Data.Length;
            }
            Debug.Assert(item.RefCount > 0);
            if (--item.RefCount <= 0)
                FreeItem(ref item);
        }

        public long DequeueToInflight(long minClock, McuCodec encoder, PrimitiveList<int> newInflightIndexes)
        {
            Debug.Assert(minClock >= 0);
            var nextClock = long.MaxValue;
            // smallest ReqClock first, terminate at item that has MinClock past now
            while (true)
            {
                if (_priorityReq.TryPeek(out var index, out _))
                {
                    ref var item = ref _items[index];
                    Debug.Assert(item.Index == index);
                    if ((item.Flags & McuCommandQueueItemFlags.Dequeued) != 0)
                    {
                        DequeuedItem(ref item);
                        _priorityReq.Dequeue();
                        continue;
                    }
                    else if (item.ReqClock - minClock > _maxFutureReqClockDuration)
                        break;
                    else if (item.MinClock <= minClock)
                    {
                        if (encoder.TryWrite(item.Data.Span))
                        {
                            if (_dumpWriter != null)
                                Dump(ref item);

                            _priorityReq.Dequeue();
                            item.RefCount++; // +inflight
                            newInflightIndexes.Add(index);
                            DequeuedItem(ref item);
                            continue;
                        }
                    }
                    if (item.MinClock < nextClock)
                        nextClock = item.MinClock;
                    if (item.ReqClock < nextClock)
                        nextClock = item.ReqClock;
                }
                break;
            }
            // than try to cram in items with smallest MinClock, terminate at item that has MinClock past now
            while (true)
            {
                if (_priorityMin.TryPeek(out var index, out _))
                {
                    ref var item = ref _items[index];
                    Debug.Assert(item.Index == index);
                    if ((item.Flags & McuCommandQueueItemFlags.Dequeued) != 0)
                    {
                        DequeuedItem(ref item);
                        _priorityMin.Dequeue();
                        continue;
                    }
                    else if (item.ReqClock - minClock > _maxFutureReqClockDuration)
                        break;
                    else if (item.MinClock <= minClock)
                    {
                        if (encoder.TryWrite(item.Data.Span))
                        {
                            if (_dumpWriter != null)
                                Dump(ref item);

                            _priorityMin.Dequeue();
                            item.RefCount++; // +inflight
                            newInflightIndexes.Add(index);
                            DequeuedItem(ref item);
                            continue;
                        }
                    }
                    if (item.MinClock < nextClock)
                        nextClock = item.MinClock;
                }
                break;
            }
#if COMMAND_QUEUE_TRACING
            if (newInflightIndexes.Count > 0)
                Trace.WriteLine($"DEQUEUE {_mcu.Alias} seq #{encoder.Seq}, dataSize={encoder.DataPostition}");
#endif
            return nextClock;
        }

        public long PeekNextClock(long minClock)
        {
            Debug.Assert(minClock >= 0);
            var nextClock = long.MaxValue;
            // smallest ReqClock first, terminate at item that has MinClock past now
            while (true)
            {
                if (_priorityReq.TryPeek(out var index, out _))
                {
                    ref var item = ref _items[index];
                    Debug.Assert(item.Index == index);
                    if ((item.Flags & McuCommandQueueItemFlags.Dequeued) != 0)
                    {
                        DequeuedItem(ref item);
                        _priorityReq.Dequeue();
                        continue;
                    }
                    else if (item.ReqClock - minClock > _maxFutureReqClockDuration)
                        break;
                    if (item.MinClock < nextClock)
                        nextClock = item.MinClock;
                    if (item.ReqClock < nextClock)
                        nextClock = item.ReqClock;
                }
                break;
            }
            // than try to cram in items with smallest MinClock, terminate at item that has MinClock past now
            while (true)
            {
                if (_priorityMin.TryPeek(out var index, out _))
                {
                    ref var item = ref _items[index];
                    Debug.Assert(item.Index == index);
                    if ((item.Flags & McuCommandQueueItemFlags.Dequeued) != 0)
                    {
                        DequeuedItem(ref item);
                        _priorityMin.Dequeue();
                        continue;
                    }
                    else if (item.ReqClock - minClock > _maxFutureReqClockDuration)
                        break;
                    if (item.MinClock < nextClock)
                        nextClock = item.MinClock;
                }
                break;
            }
            return nextClock;
        }

        private void Dump(ref McuCommandQueueItem item)
        {
            Debug.Assert(_dumpQueue != null);
            Debug.Assert(_dumpWriter != null);
            var id = item.Id;
            var minClock = item.MinClock;
            var reqClock = item.ReqClock;
            var cmd = item.CommandTemplate;
            _dumpQueue.EnqueueValue(async () =>
            {
                await _dumpWriter.WriteLineAsync($"#{id}, min={minClock}, req={reqClock}: {cmd}");
                await _dumpWriter.FlushAsync();
            }, null);
        }

        public McuCommandQueueStats GetStats()
        {
            var res = new McuCommandQueueStats();
            res.SentCount = _sentCount;
            res.SentBytes = _sentBytes;
            res.StalledCount = _stalledCount;
            res.AllocatedCount = _items.Length - _freeItems.Count;
            res.StalledBytes = _stalledBytes;
            res.InflightCount = _inflightCount;
            res.InflightBytes = _inflightBytes;
            res.TimingCriticalCount = _timingCriticalCount;
            res.UsedArenaItemCount = _arena.GetReferencedItemCount();
            res.UsedArenaCount = _arena.UsedArenas;
            res.UsedArenaBytes = _arena.UsedArenas * _arena.ArenaLength;
            res.CreatedArenaCount = _arena.CreatedArenas;
            res.CreatedArenaBytes = _arena.CreatedArenas * _arena.ArenaLength;
            return res;
        }

        public void Clear()
        {
            EmptyQueue(_priorityReq);
            EmptyQueue(_priorityMin);
            _freeItems.Clear();
            _sentCount = 0;
            _sentBytes = 0;
            _stalledCount = 0;
            _stalledBytes = 0;
            _inflightCount = 0;
            _inflightBytes = 0;
            Array.Clear(_items);
            for (int i = _items.Length - 1; i >= 0; i--)
                _freeItems.Push(i);
        }

        private void EmptyQueue<T>(PriorityQueue<int, T> queue)
        {
            while (queue.TryDequeue(out var index, out _))
            {
                ref var item = ref _items[index];
                Debug.Assert(item.Index == index);
                Debug.Assert(item.RefCount > 0);
                if (item.RefCount-- <= 0)
                    FreeItem(ref item);
            }
        }
    }
}
