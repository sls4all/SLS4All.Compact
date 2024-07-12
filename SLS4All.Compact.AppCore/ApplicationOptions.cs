// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.DependencyInjection;
using SLS4All.Compact.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SLS4All.Compact
{
    public class ApplicationOptionsDependency : IOptionsItemEnable
    {
        public string Filename { get; set; } = "";
        public string Before { get; set; } = "";
        public string After { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
    }

    public enum VideoCameraClientType
    {
        NotSet = 0,
        Fake,
        Mjpeg,
        V4L2Grayscale,
    }

    public enum TemperatureCameraClientType
    {
        NotSet = 0,
        Mlx90640Fake,
        Mlx90640Local,
    }

    public enum NetworkManagerType
    {
        NotSet = 0,
        Fake,
        DBus,
    }

    public enum PrinterClientType
    {
        NotSet = 0,
        Fake,
        McuManagerLocal,
        PipedMcuManagerProxy,
    }

    public enum PrinterPloterType
    {
        NotSet = 0,
        Fake,
        Image,
    }

    public enum McuSerialDeviceFactoryType
    {
        NotSet = 0,
        ProxySerial,
        LocalSerial,
        SshSerial,
    }

    public enum SoftHeaterType
    {
        NotSet = 0,
        SoftAnalysis,
    }

    public class ApplicationOptions
    {
        public enum PluginServiceRegistration
        {
            NotSet = 0,
            AsImplementation,
            AsImplementationAndInterfaces,
            AsImplementationAndParents,
            AsService,
        }

        public class PluginServiceInfo : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public required string Implementation { get; set; }
            public string Service { get; set; } = "";
            public PluginServiceRegistration Registration { get; set; } = PluginServiceRegistration.AsImplementationAndInterfaces;
            public ServiceLifetime Lifetime { get; set; } = ServiceLifetime.Singleton;
        }

        public class PluginOptionsInfo : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public string? Name { get; set; }
            public required string Options { get; set; }
            public required string Section { get; set; }
        }

        public class PluginReplacementInfo : IOptionsItemEnable
        {
            public bool IsEnabled { get; set; } = true;
            public required string Original { get; set; }
            public required string Replacement { get; set; }
        }

        public PrinterClientType PrinterClient { get; set; } = PrinterClientType.Fake;
        public McuSerialDeviceFactoryType McuSerialDeviceFactory { get; set; } = McuSerialDeviceFactoryType.NotSet;
        public VideoCameraClientType VideoCameraClient { get; set; } = VideoCameraClientType.Fake;
        public TemperatureCameraClientType TemperatureCameraClient { get; set; } = TemperatureCameraClientType.Mlx90640Fake;
        public NetworkManagerType NetworkManager { get; set; } = NetworkManagerType.Fake;
        public PrinterPloterType Plotter { get; set; } = PrinterPloterType.Fake;
        public SoftHeaterType SoftHeater { get; set; } = SoftHeaterType.SoftAnalysis;
        public bool UseIdealCircleHotspotCalculator { get; set; } = false;
        public bool StartBrowser { get; set; } = false;
        public string BrowserCommand { get; set; } = "";
        public string BrowserArgs { get; set; } = "";
        public Dictionary<string, ApplicationOptionsDependency> Dependencies { get; set; } = new();
        public List<string?> Includes { get; set; } = new();
        public bool PushTemperatureCameraGCode { get; set; }
        public bool Proxy { get; set; } = false;
        public int? Port { get; set; }
        public Dictionary<string, string?> PluginAssemblies { get; set; } = new();
        public Dictionary<string, PluginServiceInfo?> PluginServices { get; set; } = new();
        public Dictionary<string, PluginOptionsInfo?> PluginOptions { get; set; } = new();
        public Dictionary<string, PluginReplacementInfo?> PluginReplacements { get; set; } = new();
    }
}
