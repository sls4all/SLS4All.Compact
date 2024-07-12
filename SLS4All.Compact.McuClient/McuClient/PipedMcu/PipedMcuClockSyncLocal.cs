// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    public sealed class PipedMcuClockSyncLocal : McuClockSyncAbstract
    {
        private PipedMcuLocal? _mcu;

        public PipedMcuClockSyncLocal(ILogger<PipedMcuClockSyncLocal> logger, McuManager root) 
            : base(logger, root)
        {
        }

        public override Task Start(IMcu mcu, TaskScheduler scheduler, CancellationToken cancel)
        {
            _mcu = (PipedMcuLocal)mcu;
            return base.Start(mcu, scheduler, cancel);
        }

        protected override void OnException(Exception ex)
        {
            base.OnException(ex);
            if (_mcu != null)
                _mcu.SendClockSyncExceptionEvent(ex);
        }

        protected override void OnUnreachableChanged()
        {
            base.OnUnreachableChanged();
            if (_mcu != null)
                _mcu.SendClockSyncUnreachableEvent();
        }

        protected override void OnStateChanged()
        {
            base.OnStateChanged();
            if (_mcu != null)
            {
                var state = CurrentState;
                var clockEst = state.ClockEst;
                _mcu.SendClockSyncEvent(clockEst.SampleTime, clockEst.Clock, clockEst.Freq, state.IsReady);
            }
        }
    }
}
