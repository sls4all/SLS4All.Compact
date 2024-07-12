// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient
{
    public class McuConfig
    {
        private static readonly char[] s_numbers = new[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9' };

        public string McuName { get; private set; } = "";
        public string BuildVersions { get; private set; } = "";
        public string Version { get; private set; } = "";
        public IDictionary<string, int> Commands { get; set; } = new Dictionary<string, int>();
        public IDictionary<string, object> Constants { get; set; } = new Dictionary<string, object>();
        public IDictionary<string, IDictionary<string, int>> Enumerations { get; set; } = new Dictionary<string, IDictionary<string, int>>();
        public IDictionary<string, int> Responses { get; set; } = new Dictionary<string, int>();

        public IDictionary<string, McuCommand> NameToCommand { get; set; } = new Dictionary<string, McuCommand>();
        public IDictionary<string, McuCommand> StringToCommand { get; set; } = new Dictionary<string, McuCommand>();
        public IDictionary<int, McuCommand> IdToCommand { get; set; } = new Dictionary<int, McuCommand>();
        public IDictionary<int, string> IdToResponse { get; set; } = new Dictionary<int, string>();
        public IDictionary<int, string> IdToStaticString { get; set; } = new Dictionary<int, string>();
        public IDictionary<string, int> PinToId { get; set; } = new Dictionary<string, int>();
        public IDictionary<string, int> BusToId { get; set; } = new Dictionary<string, int>();
        public IDictionary<string, int> ThermocoupleTypeToId { get; set; } = new Dictionary<string, int>();
        public bool IsDefault { get; set; }

        public static McuConfig Parse(string mcuAlias, Stream stream)
        {
            var res = new McuConfig();
            var doc = JsonDocument.Parse(stream);
            if (doc.RootElement.TryGetProperty("build_versions", out var buildVersions) && buildVersions.ValueKind == JsonValueKind.String)
                res.BuildVersions = buildVersions.GetString()!;
            if (doc.RootElement.TryGetProperty("version", out var version) && version.ValueKind == JsonValueKind.String)
                res.Version = version.GetString()!;
            if (doc.RootElement.TryGetProperty("commands", out var commands) && commands.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in commands.EnumerateObject())
                {
                    if (pair.Value.ValueKind == JsonValueKind.Number)
                        res.Commands.Add(pair.Name, pair.Value.GetInt32());
                }
            }
            if (doc.RootElement.TryGetProperty("config", out var config) && config.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in config.EnumerateObject())
                {
                    if (pair.Value.ValueKind == JsonValueKind.Number)
                        res.Constants.Add(pair.Name, pair.Value.GetInt32());
                    else if (pair.Value.ValueKind == JsonValueKind.String)
                        res.Constants.Add(pair.Name, pair.Value.GetString()!);
                }
            }
            if (doc.RootElement.TryGetProperty("enumerations", out var enumerations) && enumerations.ValueKind == JsonValueKind.Object)
            {
                foreach (var enumeration in enumerations.EnumerateObject())
                {
                    if (enumeration.Value.ValueKind == JsonValueKind.Object)
                    {
                        var values = new Dictionary<string, int>();
                        res.Enumerations.Add(enumeration.Name, values);
                        foreach (var item in enumeration.Value.EnumerateObject())
                        {
                            if (item.Value.ValueKind == JsonValueKind.Number)
                                values.Add(item.Name, item.Value.GetInt32());
                            else if (item.Value.ValueKind == JsonValueKind.Array)
                            {
                                var bounds = item.Value.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.Number).Select(x => x.GetInt32()).ToArray();
                                if (bounds.Length == 2)
                                {
                                    for (int i = bounds[0], e = i + bounds[1], s = 0; i < e; i++, s++)
                                        values.Add(RewriteEnumerationItem(item.Name, s), i);
                                }
                            }
                        }
                    }
                }
            }
            if (doc.RootElement.TryGetProperty("responses", out var responses) && responses.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in responses.EnumerateObject())
                {
                    if (pair.Value.ValueKind == JsonValueKind.Number)
                        res.Responses.Add(pair.Name, pair.Value.GetInt32());
                }
            }
            res.Prepare(mcuAlias);
            return res;
        }

        private static string RewriteEnumerationItem(string name, int i)
        {
            var start = name.LastIndexOfAny(s_numbers);
            if (start != -1)
                name = name.Substring(0, start);
            name += i.ToString();
            return name;
        }

        public McuConfig Prepare(string mcuName)
        {
            McuName = mcuName;
            foreach (var pair in Commands.Concat(Responses))
            {
                var command = McuCommand.Parse(pair.Value, pair.Key);
                NameToCommand.Add(command.CommandName, command);
                StringToCommand.Add(command.MessageFormat, command);
                if (command.MessageFormat != command.CommandName)
                    StringToCommand.Add(command.CommandName, command);
                IdToCommand.Add(command.CommandId, command);
            }
            if (Enumerations.TryGetValue("static_string_id", out var staticStrings))
            {
                foreach (var pair in staticStrings)
                    IdToStaticString.Add(pair.Value, pair.Key);
            }
            if (Enumerations.TryGetValue("pin", out var pins))
            {
                foreach (var pair in pins)
                    PinToId.Add(pair.Key, pair.Value);
            }
            if (Enumerations.TryGetValue("spi_bus", out var spiBuses))
            {
                foreach (var pair in spiBuses)
                    BusToId.Add(pair.Key, pair.Value);
            }
            if (Enumerations.TryGetValue("thermocouple_type", out var thermocoupleTypes))
            {
                foreach (var pair in thermocoupleTypes)
                    ThermocoupleTypeToId.Add(pair.Key, pair.Value);
            }

            // freeze for some performance
            Commands = Commands.ToFrozenDictionary();
            Constants = Constants.ToFrozenDictionary();
            Enumerations = Enumerations.ToDictionary(x => x.Key, x => (IDictionary<string, int>)x.Value.ToFrozenDictionary()).ToFrozenDictionary();
            Responses = Responses.ToFrozenDictionary();
            NameToCommand = NameToCommand.ToFrozenDictionary();
            StringToCommand = StringToCommand.ToFrozenDictionary();
            IdToCommand = IdToCommand.ToFrozenDictionary();
            IdToResponse = IdToResponse.ToFrozenDictionary();
            IdToStaticString = IdToStaticString.ToFrozenDictionary();
            PinToId = PinToId.ToFrozenDictionary();
            BusToId = BusToId.ToFrozenDictionary();
            ThermocoupleTypeToId = ThermocoupleTypeToId.ToFrozenDictionary();
            return this;
        }

        public int GetConstInt32(string name)
        {
            if (!Constants.TryGetValue(name, out var value))
                throw new ArgumentException($"Constant {name} was not found on MCU {McuName}");
            if (value is not int int32)
                throw new ArgumentException($"Constant {name} is not Int32 on MCU {McuName}");
            return int32;
        }

        public int GetPin(string name)
        {
            if (!PinToId.TryGetValue(name, out var value))
                throw new ArgumentException($"Pin {name} was not found on MCU {McuName}");
            return value;
        }

        public int GetBus(string name)
        {
            if (!BusToId.TryGetValue(name, out var value))
                throw new ArgumentException($"Bus {name} was not found on MCU {McuName}");
            return value;
        }

        public int GetThermocoupleType(string name)
        {
            if (!ThermocoupleTypeToId.TryGetValue(name, out var value))
                throw new ArgumentException($"Thermocouple type {name} was not found on MCU {McuName}");
            return value;
        }
    }
}
