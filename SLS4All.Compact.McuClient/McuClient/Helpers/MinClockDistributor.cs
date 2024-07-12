// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.McuClient.Pins;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Helpers
{
    public sealed class MinClockDistributor
    {
        private readonly PrimitiveDeque<(int First, int Last)> _todo;
        private readonly PrimitiveList<(long ReqClock /* sort first */, int Discriminator, long MinClock)> _clocks;
        private readonly Dictionary<(long MinClock, int Discriminator), long> _map;
        private readonly McuMinClockFunc _clocksAdd;
        private readonly McuMinClockFunc _clocksGet;
        private int _discriminator;

        public McuMinClockFunc ClocksAdd => _clocksAdd;
        public McuMinClockFunc ClocksGet => _clocksGet;
        public int Discriminator
        {
            get => _discriminator;
            set => _discriminator = value;
        }

        public MinClockDistributor()
        {
            _todo = new();
            _clocks = new();
            _map = new();

            _clocksAdd = (minClock, reqClock) =>
            {
                var item = (reqClock, _discriminator, minClock);
                if (_clocks.Count == 0 || _clocks[^1] != item)
                    _clocks.Add(item);
                return minClock;
            };
            _clocksGet = (minClock, reqClock) => _map[(reqClock, _discriminator)];
        }

        public void Clear()
        {
            _clocks.Clear();
            _map.Clear();
        }

        public void Process(long minSendAheadClock, long maxSendAheadClock)
        {
            var clocks = _clocks;
            var map = _map;
            var span = clocks.Span;
            map.Clear();
            if (span.Length <= 2 || minSendAheadClock == maxSendAheadClock)
            {
                for (int i = 0; i < span.Length; i++)
                {
                    ref var item = ref span[i];
                    map[(item.ReqClock, item.Discriminator)] = item.MinClock;
                }
                return;
            }
            var todo = _todo;
            todo.Clear();
            span.Sort();

            todo.PushBack((0, span.Length - 1));

            // find peaks
            while (todo.Count > 0)
            {
                (var first, var last) = todo.PopFront();
                Debug.Assert(first != last);
                var mostAheadIndex = -1;
                var mostAheadValue = long.MinValue;
                var mostBehindIndex = -1;
                var mostBehindValue = long.MaxValue;
                var minReq = span[first].ReqClock;
                var maxReq = span[last].ReqClock;
                var reqDuration = maxReq - minReq;
                var range = last - first;
                for (int i = first; i <= last; i++)
                {
                    var oldReqValue = span[i].ReqClock;
                    var newReqValue = minReq + reqDuration * (i - first) / range;
                    var aheadValue = newReqValue - oldReqValue;
                    if (aheadValue > mostAheadValue)
                    {
                        mostAheadValue = aheadValue;
                        mostAheadIndex = i;
                    }
                    if (aheadValue < mostBehindValue)
                    {
                        mostBehindValue = aheadValue;
                        mostBehindIndex = i;
                    }
                }
                if (mostAheadValue > 0)
                {
                    // subdivide
                    Debug.Assert(mostAheadIndex != first && mostAheadIndex != last);
                    todo.PushBack((first, mostAheadIndex));
                    todo.PushBack((mostAheadIndex, last));
                }
                else if (mostBehindValue < -maxSendAheadClock)
                {
                    // subdivide
                    Debug.Assert(mostBehindIndex != first && mostBehindIndex != last);
                    todo.PushBack((first, mostBehindIndex));
                    todo.PushBack((mostBehindIndex, last));
                }
                else
                {
                    // evenly distribute
                    for (int i = first; i <= last; i++)
                    {
                        ref var oldValue = ref clocks[i];
                        var interpolatedReq = minReq + reqDuration * (i - first) / range;
                        Debug.Assert(interpolatedReq <= oldValue.ReqClock);
                        map[(oldValue.ReqClock, oldValue.Discriminator)] = interpolatedReq - minSendAheadClock;
                    }
                }
            }

#if DEBUG
            {
                ref var firstItem = ref span[0];
                ref var lastItem = ref span[^1];
                for (int i = 0; i < span.Length; i++)
                {
                    ref var oldItem = ref clocks[i];
                    var newMinClock = map[(oldItem.ReqClock, oldItem.Discriminator)];

                    Debug.Assert(newMinClock <= oldItem.ReqClock - minSendAheadClock);
                    Debug.Assert(i != 0 || i + 1 != span.Length || newMinClock == oldItem.ReqClock - minSendAheadClock);
                }
            }
#endif
        }
    }
}
