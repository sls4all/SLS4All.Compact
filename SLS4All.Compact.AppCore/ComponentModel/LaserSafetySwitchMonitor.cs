// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.IO;
using SLS4All.Compact.Power;
using SLS4All.Compact.Printing;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.ComponentModel
{
    public class LaserSafetySwitchMonitorOptions
    {
        public TimeSpan LaserPowerCheckDuration { get; set; } = TimeSpan.FromSeconds(1); // just some relatively large value, larger than potential powermanager low frequency period
    }

    public sealed class LaserSafetySwitchMonitor : IDelayedConstructable, IDisposable
    {
        private readonly IOptionsMonitor<LaserSafetySwitchMonitorOptions> _options;
        private readonly IToastProvider _toastProvider;
        private readonly IInputClient _inputClient;
        private readonly IPowerClient _powerClient;
        private readonly IObjectFactory<IPrintingService, object> _printingServiceFactory;
        private volatile bool _shown;
        private volatile bool _shownWhilePrinting;

        public LaserSafetySwitchMonitor(
            IOptionsMonitor<LaserSafetySwitchMonitorOptions> options,
            IToastProvider toastProvider,
            IInputClient inputClient,
            IPowerClient powerClient,
            IObjectFactory<IPrintingService, object> printingServiceFactory)
        {
            _options = options;
            _toastProvider = toastProvider;
            _inputClient = inputClient;
            _powerClient = powerClient;
            _printingServiceFactory = printingServiceFactory;

            _powerClient.StateChangedHighFrequency.AddHandler(OnPowerState);
            _inputClient.StateChangedHighFrequency.AddHandler(OnInputState);
        }

        public void Dispose()
        {
            _powerClient.StateChangedHighFrequency.RemoveHandler(OnPowerState);
            _inputClient.StateChangedHighFrequency.RemoveHandler(OnInputState);
        }

        private ValueTask OnInputState(InputState arg1, CancellationToken cancel)
        {
            if (_inputClient.CurrentState.TryGetEntry(_inputClient.SafeButtonId, out var safe) &&
                safe.Value == true)
                _shown = false;
            return ValueTask.CompletedTask;
        }

        private ValueTask OnPowerState(PowerState state, CancellationToken cancel)
        {
            var options = _options.CurrentValue;
            if (_powerClient.TryGetRecentPower(
                    _powerClient.LaserId, 
                    out _, 
                    out var laserPower, 
                    duration: options.LaserPowerCheckDuration) &&
                laserPower > 0 &&
                _inputClient.CurrentState.TryGetEntry(_inputClient.SafeButtonId, out var safe) &&
                safe.Value == false)
            {
                if (!_shown)
                { 
                    _shown = true;
                    _toastProvider.Show(new ToastMessage
                    {
                        Type = ToastMessageType.Warning,
                        HeaderText = "Safety switch error",
                        BodyText = "The laser cannot be turned on since the printer lid or powder bin is open. If a printing is in progress, it will probably be incomplete. It may also be warped due to rapid cooling if the lid has been opened mid-printing.",
                        Key = this,
                    });
                }
            }
            // reset `shown` if stopped printing
            if (_shown)
            {
                using (var printingService = _printingServiceFactory.CreateDisposable())
                {
                    var isPrinting = printingService.Instance.IsPrinting;
                    if (isPrinting)
                        _shownWhilePrinting = true;
                    else if (_shownWhilePrinting)
                    {
                        _shownWhilePrinting = false;
                        _shown = false;
                    }
                }
            }
            return ValueTask.CompletedTask;
        }
    }
}
