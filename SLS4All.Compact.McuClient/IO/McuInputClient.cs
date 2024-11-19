// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Movement;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printer;
using SLS4All.Compact.McuClient;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.IO
{
    public class McuInputClientOptions
    {
        public TimeSpan LowFrequencyPeriod { get; set; } = TimeSpan.FromSeconds(1);
        public string SafeButtonName { get; set; } = "safe";
        public string LidClosedName { get; set; } = "safe";
    }

    public sealed class McuInputClient : BackgroundThreadService, IInputClient
    {
        private readonly ILogger<McuInputClient> _logger;
        private readonly IOptionsMonitor<McuInputClientOptions> _options;
        private readonly PeriodicTimer _lowFrequencyTimer;
        private readonly string _safeButtonId;
        private readonly string _lidClosedId;

        private readonly Lock _stateLock = new();
        private volatile InputState _lowFrequencyState;
        private readonly Dictionary<string, (int value, SystemTimestamp timestamp)> _stateDict;

        public InputState CurrentState => _lowFrequencyState;
        public AsyncEvent<InputState> StateChangedLowFrequency { get; } = new();
        public AsyncEvent<InputState> StateChangedHighFrequency { get; } = new();
        public string SafeButtonId => _safeButtonId;
        public string LidClosedId => _lidClosedId;

        public McuInputClient(
            ILogger<McuInputClient> logger,
            IOptionsMonitor<McuInputClientOptions> options,
            McuPrinterClient printerClient)
            : base(logger)
        {
            _logger = logger;
            _options = options;

            var o = options.CurrentValue;
            _safeButtonId = o.SafeButtonName;
            _lidClosedId = o.LidClosedName;
            _lowFrequencyState = new(Array.Empty<InputEntry>());
            _lowFrequencyTimer = new PeriodicTimer(o.LowFrequencyPeriod);
            _stateDict = new();

            printerClient.ManagerSetEvent.AddHandler(OnManagerSet);
        }

        private ValueTask OnManagerSet(McuManager manager, CancellationToken token)
        {
            lock (_stateLock)
            {
                var now = SystemTimestamp.Now;
                foreach (var pair_ in manager.Buttons)
                {
                    var pair = pair_;
                    _stateDict[pair.Key] = (0, now);
                    pair.Value.ButtonEvent.AddHandler((value, cancel) => OnButtonEvent(pair.Key, value, cancel));
                }
            }
            return ValueTask.CompletedTask;
        }

        private ValueTask OnButtonEvent(string id, int value, CancellationToken cancel)
        {
            lock (_stateLock)
            {
                var now = SystemTimestamp.Now;
                _stateDict[id] = (value, now);
            }
            var state = GetState();
            return StateChangedHighFrequency.Invoke(state, cancel);
        }

        private InputEntry[] ReadEntriesNeedsLock()
        {
            var entries = new InputEntry[_stateDict.Count];
            var i = 0;
            foreach ((var key, var value) in _stateDict)
                entries[i++] = new InputEntry(value.timestamp, key, value.value != 0);
            return entries;
        }

        private InputState GetState()
        {
            lock (_stateLock)
            {
                var entries = ReadEntriesNeedsLock();
                Array.Sort(entries, (x, y) => StringComparer.OrdinalIgnoreCase.Compare(x.Id, y.Id));
                return new InputState(entries);
            }
        }

        protected override async Task ExecuteTaskAsync(CancellationToken cancel)
        {
            while (true)
            {
                try
                {
                    var state = GetState();
                    _lowFrequencyState = state;
                    await StateChangedLowFrequency.Invoke(state, cancel);
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to get/process low frequency data");
                }
                try
                {
                    await _lowFrequencyTimer.WaitForNextTickAsync(cancel);
                }
                catch (Exception ex)
                {
                    if (cancel.IsCancellationRequested)
                        break;
                    _logger.LogError(ex, $"Failed to wait for next period");
                }
            }
        }
    }
}
