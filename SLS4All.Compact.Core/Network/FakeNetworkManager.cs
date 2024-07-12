// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Transactions;

namespace SLS4All.Compact.Network
{
    public class FakeNetworkManagerOptions
    {
        public TimeSpan Delay { get; set; } = TimeSpan.FromSeconds(2);
    }

    public sealed class FakeNetworkManager : INetworkManager
    {
        private abstract class FakeNetworkDevice : INetworkDevice
        {
            protected readonly FakeNetworkManager _manager;
            private volatile NetworkAddressSetup? _assignedSetup;
            private volatile NetworkAddressSettings? _addressSettings;

            public required string DhcpNetworkPrefix { get; init; }

            public required string Name { get; init; }

            public required string HwAddress { get; init; }

            public virtual bool IsConnected => _addressSettings != null;

            public FakeNetworkDevice(FakeNetworkManager manager)
            {
                _manager = manager;
                _addressSettings = default!;
            }

            public Task<NetworkAddressSettings?> GetAddressSettings(CancellationToken cancel)
                => Task.FromResult(_addressSettings);

            public Task SetAddressSettings(NetworkAddressSettings settings, CancellationToken cancel)
            {
                if (settings is NetworkAddressSettingsDhcp)
                {
                    _assignedSetup = new NetworkAddressSetup
                    {
                        Addresses = new[]
                        {
                            new NetworkAddressTriple
                            {
                                 IPAddress = IPAddress.Parse(DhcpNetworkPrefix + ".100"),
                                 Prefix = 24,
                                 Gateway = IPAddress.Parse(DhcpNetworkPrefix + ".1"),
                            }
                        },
                        Dns = new[] { IPAddress.Parse(DhcpNetworkPrefix + ".1") },
                    };
                }
                else if (settings is NetworkAddressSettingsStatic staticSettings)
                {
                    _addressSettings = new NetworkAddressSettingsStatic
                    {
                        Setup = staticSettings.Setup,
                    };
                }
                else
                    throw new ArgumentException($"Invalid settings", nameof(settings));
                return Task.CompletedTask;
            }

            public Task<bool> GetIsConnected(CancellationToken cancel)
                => Task.FromResult(IsConnected);

            public Task<NetworkAddressSetup?> GetAssignedAddress(CancellationToken cancel)
                => Task.FromResult(_assignedSetup);

            public abstract Task Disconnect(CancellationToken cancel);
        }

        private class FakeWiredNetworkDevice : FakeNetworkDevice, IWiredNetworkDevice
        {
            public FakeWiredNetworkDevice(FakeNetworkManager manager) : base(manager)
            {
            }
            
            public override Task Disconnect(CancellationToken cancel)
                => Task.CompletedTask;
        }

        private record class FakeWirelessNetwork : WirelessNetwork
        {
            public required string? Password { get; init; }
        }

        private class WirelessNetworkDevice : FakeNetworkDevice, IWirelessNetworkDevice
        {
            private volatile WirelessNetwork? _currentNetwork;

            public WirelessNetwork? CurrentNetwork
            {
                get => _currentNetwork;
                set => _currentNetwork = value;
            }

            public override bool IsConnected => base.IsConnected && _currentNetwork != null;

            public WirelessNetworkDevice(FakeNetworkManager manager) : base(manager)
            {
            }

            public Task<WirelessConnectParameters> GetConnectParameters(WirelessNetwork network, CancellationToken cancel)
                => Task.FromResult(new WirelessConnectParameters());

            public async Task ConnectToNetwork(WirelessNetwork network, WirelessConnectParameters args, CancellationToken cancel)
            {
                var tnetwork = (FakeWirelessNetwork)network;
                await Task.Delay(_manager._options.CurrentValue.Delay);
                if (tnetwork.Password != null &&
                    tnetwork.Password != args.Password)
                    throw new AuthenticationException("Network authentication failed");
                _currentNetwork = tnetwork with { };
                if (args.Address != null)
                    await ((INetworkDevice)this).SetAddressSettings(args.Address, cancel);
            }

            public override async Task Disconnect(CancellationToken cancel)
            {
                if (_currentNetwork == null)
                    return;
                await Task.Delay(_manager._options.CurrentValue.Delay);
                _currentNetwork = null;
            }

            public async Task<WirelessNetworkInfo[]> GetAvailableNetworks(CancellationToken cancel)
            {
                await Task.Delay(_manager._options.CurrentValue.Delay);
                return new WirelessNetworkInfo[]
                {
                    WirelessNetworkInfo.Create(new FakeWirelessNetwork{ Identifier = "1", Address = "1", Name = "praha5-free", IsSecure = false, Password = null, SignalPercent = 25 }, _currentNetwork),
                    WirelessNetworkInfo.Create(new FakeWirelessNetwork{ Identifier = "2", Address = "2", Name = "anyteq", IsSecure = true, Password = "anyteq", SignalPercent = 100 }, _currentNetwork),
                    WirelessNetworkInfo.Create(new FakeWirelessNetwork{ Identifier = "3", Address = "3", Name = "HP LaserJet", IsSecure = true, Password = "HP", SignalPercent = 75 }, _currentNetwork),
                    WirelessNetworkInfo.Create(new FakeWirelessNetwork{ Identifier = "4", Address = "4", Name = "FBISurveillanceVan", IsSecure = true, Password = "fbi", SignalPercent = 0 }, _currentNetwork),
                    WirelessNetworkInfo.Create(new FakeWirelessNetwork{ Identifier = "5", Address = "F1-F1-75-F4-87-CB", Name = null, IsSecure = true, Password = "hidden", SignalPercent = 0 }, _currentNetwork),
                };
            }

            public Task<WirelessNetworkInfo?> GetCurrentNetwork(CancellationToken cancel)
                => Task.FromResult(_currentNetwork != null ? new WirelessNetworkInfo { Network = _currentNetwork, CanForget = true } : null);

            public Task ForgetNetwork(WirelessNetwork network, CancellationToken cancel)
                => Task.CompletedTask;
        }

        private readonly IOptionsMonitor<FakeNetworkManagerOptions> _options;
        private readonly INetworkDevice[] _devices;

        public FakeNetworkManager(
            IOptionsMonitor<FakeNetworkManagerOptions> options)
        {
            _options = options;
            _devices = new INetworkDevice[]
            {
                new FakeWiredNetworkDevice(this)
                {
                     DhcpNetworkPrefix = "192.168.1",
                     HwAddress = "45:DF:16:52:F8:67",
                     Name = "ethernet",
                },
                new FakeWiredNetworkDevice(this)
                {
                     DhcpNetworkPrefix = "192.168.2",
                     HwAddress = "E6:1D:7A:C9:E0:4A",
                     Name = "usb-ethernet",
                },
                new WirelessNetworkDevice(this)
                {
                     DhcpNetworkPrefix = "192.168.3",
                     HwAddress = "A3:27:65:82:0E:EA",
                     Name = "wifi",
                },
            };
        }

        public Task<INetworkDevice[]> GetAllDevices(CancellationToken cancel)
            => Task.FromResult(_devices);
    }
}
