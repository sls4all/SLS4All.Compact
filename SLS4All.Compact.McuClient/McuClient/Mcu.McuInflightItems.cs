// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using System.Diagnostics;
using System.Linq;
using System.Numerics;

namespace SLS4All.Compact.McuClient
{
    public partial class McuAbstract
    {
        [Flags]
        private enum InflightItemFlags
        {
            NotSet = 0,
            Retransmitted = 1,
        }

        private struct InflightItem
        {
            public ulong Seq;
            public InflightItemFlags Flags;
            public int ItemIndex;
            public SystemTimestamp SendTimestamp;
        }

        private sealed class McuInflightItems : IMcuInflightItems
        {
            private readonly McuAbstract _mcu;
            private readonly PrimitiveList<InflightItem> _inflightItems;
            private readonly PrimitiveList<int> _dequeuedItems;
            private readonly HashSet<ulong> _ids;
            private readonly Timer _retransmitTimer;

            public int Count
            {
                get
                {
                    lock (_mcu._commandQueueNeedsLock)
                    {
                        return _inflightItems.Count;
                    }
                }
            }

            public McuInflightItems(McuAbstract mcu)
            {
                _mcu = mcu;
                _inflightItems = new();
                _dequeuedItems = new();
                _ids = new();
                _retransmitTimer = new Timer(OnRetransmit);
            }

            public TimeSpan FeedCommandsFromQueue(SystemTimestamp now, McuCodec codec, out int dequeuedCount)
            {
                lock (_mcu._commandQueueNeedsLock)
                {
                    var clock = _mcu._clockSync.IsReady ? _mcu._clockSync.GetClock(now) : 0;
#if DEBUG
                    if (McuCommandQueue.IsTracing)
                        Trace.WriteLine($"DEQUEING at {now} (clock={clock})");
#endif
                    var inflightWasEmpty = _inflightItems.Count == 0;
                    ulong? minInflightSeq = null;
                    if (_inflightItems.Count > 0)
                        minInflightSeq = _inflightItems[0].Seq;
                    var currentSeq = codec.Seq;
                    var toWait = TimeSpan.MaxValue;
                    if (minInflightSeq == null || (int)(currentSeq - minInflightSeq.Value + 1) <= _mcu._maxInflightBlocks)
                    {
                        _dequeuedItems.Clear();
                        var nextClock = _mcu._commandQueueNeedsLock.Dequeue(clock, codec, _dequeuedItems);
                        if (nextClock != long.MaxValue)
                        {
                            var clockDuration = nextClock - clock;
                            toWait = clockDuration != 0 ? _mcu._clockSync.GetSpanDuration(clockDuration) : TimeSpan.Zero;
                        }
                        var itemsDequeued = _dequeuedItems.Span;
                        for (int i = 0; i < itemsDequeued.Length; i++)
                            _inflightItems.Add() = new() { Seq = currentSeq, ItemIndex = itemsDequeued[i], SendTimestamp = now };
                        dequeuedCount = itemsDequeued.Length;
                    }
                    else
                        dequeuedCount = 0;
                    if (inflightWasEmpty && dequeuedCount > 0)
                        RescheduleTimerNeedsLock();
                    return toWait;
                }
            }

            public (SystemTimestamp Now, SystemTimestamp Next) GetNextWaitTimestamp()
            {
                lock (_mcu._commandQueueNeedsLock)
                {
                    var now = SystemTimestamp.Now;
                    var clock = _mcu._clockSync.IsReady ? _mcu._clockSync.GetClock(now) : 0;
                    var nextClock = _mcu._commandQueueNeedsLock.Dequeue(clock, null, null);
                    if (nextClock <= clock) // nextClock might be 0 for immediate commands
                        return (now, now);
                    var maxTimestamp = now + _maxSendWaitPeriod;
                    if (nextClock != long.MaxValue)
                    {
                        var nextTimestamp = _mcu._clockSync.GetTimestamp(nextClock);
                        if (nextTimestamp <= maxTimestamp)
                            return (now, nextTimestamp);
                    }
                    return (now, maxTimestamp);
                }
            }

            public void AckItems(
                ulong receiveSeq,
                Dictionary<string, SystemTimestamp> lastSendTimes)
            {
                lock (_mcu._commandQueueNeedsLock)
                {
                    var hasChangedFirstInflight = false;
                    _ids.Clear();
                    for (int itry = 0; itry < 2; itry++)
                    {
                        var inflightSpan = _inflightItems.Span;
                        for (int i = inflightSpan.Length - 1; i >= 0; i--)
                        {
                            ref var inflightItem = ref inflightSpan[i];
                            ref var item = ref _mcu._commandQueueNeedsLock[inflightItem.ItemIndex];
                            if (inflightItem.Seq < receiveSeq || _ids.Contains(item.Id))
                            {
                                if ((item.Flags & McuCommandQueueItemFlags.Retransmit) == 0)
                                    lastSendTimes[item.CommandTemplate.MessageFormat] = inflightItem.SendTimestamp;
                                _ids.Add(item.Id);
                                _mcu._commandQueueNeedsLock.AckItem(inflightItem.ItemIndex);
                                _inflightItems.RemoveAt(i);
                                if (i == 0)
                                    hasChangedFirstInflight = true;
                            }
                        }
                        if (_ids.Count == 0)
                            break;
                    }
                    if (hasChangedFirstInflight)
                        RescheduleTimerNeedsLock();
                }
            }

            private void RescheduleTimerNeedsLock()
            {
                return; // NOTE: retransmits disabled for now
                //if (_inflightItems.Count == 0)
                //    _retransmitTimer.Change(-1, -1);
                //else
                //{
                //    var delay = _retransmitTimeout - _inflightItems[0].SendTimestamp.ElapsedFromNow;
                //    if (delay < TimeSpan.Zero)
                //        delay = TimeSpan.Zero;
                //    _retransmitTimer.Change(delay, Timeout.InfiniteTimeSpan);
                //}
            }

            private void OnRetransmit(object? state)
            {
                return; // NOTE: retransmits disabled for now
                //lock (_mcu._commandQueueNeedsLock)
                //{
                //    var timestamp = SystemTimestamp.Now;
                //    _ids.Clear();
                //    for (int itry = 0; itry < 2; itry++)
                //    {
                //        var inflightItems = _inflightItems.Span;
                //        for (int i = inflightItems.Length - 1; i >= 0; i--)
                //        {
                //            ref var inflightItem = ref inflightItems[i];
                //            var elapsed = timestamp - inflightItem.SendTimestamp;
                //            if (elapsed > _retransmitTimeout &&
                //                elapsed < _retransmitMaxPeriod &&
                //                (inflightItem.Flags & InflightItemFlags.Retransmitted) == 0)
                //            {
                //                // NOTE: each inflight item needs to be retransmitted just once, and that will duplicate it whem dequeued again
                //                inflightItem.Flags |= InflightItemFlags.Retransmitted; 
                //                _mcu._commandQueueNeedsLock.NakItem(inflightItem.ItemIndex);
                //            }
                //        }
                //        if (_ids.Count == 0)
                //            break;
                //    }
                //    RescheduleTimerNeedsLock();
                //}
            }

            public void Clear()
            {
                lock (_mcu._commandQueueNeedsLock)
                {
                    _inflightItems.Clear();
                    RescheduleTimerNeedsLock();
                }
            }

            public void Dispose()
            {
                _retransmitTimer.Dispose();
            }
        }
    }
}
