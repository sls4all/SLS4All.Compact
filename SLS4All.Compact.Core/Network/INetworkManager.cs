// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Network
{
    public abstract record class NetworkAddressSettings
    {
    }

    public record class NetworkAddressTriple
    {
        public required IPAddress IPAddress { get; init; }
        public required int Prefix { get; init; }
        public required IPAddress Gateway { get; init; }
    }

    public record class NetworkAddressSetup
    {
        public required NetworkAddressTriple[] Addresses { get; init; }
        public required IPAddress[] Dns { get; init; }
    }

    public record class NetworkAddressSettingsDhcp : NetworkAddressSettings
    {
    }

    public record class NetworkAddressSettingsStatic : NetworkAddressSettings
    {
        public required NetworkAddressSetup Setup { get; init; }
    }

    public interface INetworkDevice
    {
        string Name { get; }

        Task Disconnect(CancellationToken cancel);
        Task<bool> GetIsConnected(CancellationToken cancel);
        Task SetAddressSettings(NetworkAddressSettings settings, CancellationToken cancel);
        Task<NetworkAddressSettings?> GetAddressSettings(CancellationToken cancel);
        Task<NetworkAddressSetup?> GetAssignedAddress(CancellationToken cancel);
    }

    public record class WirelessNetwork
    {
        public required string Identifier { get; init; }
        public required string? Name { get; init; }
        public bool IsVisible => !string.IsNullOrWhiteSpace(Name);
        public required string Address { get; init; }
        public string NameOrAddress => IsVisible ? Name! : Address;
        public required bool IsSecure { get; init; }
        public required float SignalPercent { get; init; }
    }

    public record class WirelessNetworkInfo
    {
        public required WirelessNetwork Network { get; init; }
        public bool CanForget { get; init; }

        public static WirelessNetworkInfo Create(WirelessNetwork network, WirelessNetwork? current)
            => new WirelessNetworkInfo
            {
                Network = network,
                CanForget = network == current,
            };
    }

    public class WirelessConnectParameters
    {
        public string? Password { get; set; }
        public NetworkAddressSettings? Address { get; set; }
    }

    public interface IWiredNetworkDevice : INetworkDevice
    {
    }

    public interface IWirelessNetworkDevice : INetworkDevice
    {
        Task ForgetNetwork(WirelessNetwork network, CancellationToken cancel);
        Task<WirelessNetworkInfo?> GetCurrentNetwork(CancellationToken cancel);
        Task<WirelessNetworkInfo[]> GetAvailableNetworks(CancellationToken cancel);
        Task<WirelessConnectParameters> GetConnectParameters(WirelessNetwork network, CancellationToken cancel);
        Task ConnectToNetwork(WirelessNetwork network, WirelessConnectParameters args, CancellationToken cancel);
    }

    public interface INetworkManager
    {
        Task<INetworkDevice[]> GetAllDevices(CancellationToken cancel);
    }
}
