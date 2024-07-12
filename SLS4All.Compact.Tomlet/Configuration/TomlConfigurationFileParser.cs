// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tomlet;
using Tomlet.Models;

namespace SLS4All.Compact.Configuration
{
    internal sealed class TomlConfigurationFileParser
    {
        private TomlConfigurationFileParser() { }

        private readonly Dictionary<string, string?> _data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        public static IDictionary<string, string?> Parse(Stream input)
            => new TomlConfigurationFileParser().ParseStream(input);

        private Dictionary<string, string?> ParseStream(Stream input)
        {
            var parser = new TomlParser();

            using (var reader = new StreamReader(input))
            {
                var doc = parser.Parse(reader.ReadToEnd());
                VisitTable("", doc);
            }

            return _data;
        }

        private void VisitArray(string key, TomlArray array)
        {
            var index = 0;
            foreach (var item in array.ArrayValues)
            {
                var indexStr = (index++).ToString();
                var subKey = key == "" ? indexStr : key + ConfigurationPath.KeyDelimiter + indexStr;
                Visit(subKey, item);
            }
        }

        private void VisitTable(string key, TomlTable table)
        {
            foreach (var item in table.Entries)
            {
                var subKey = key == "" ? item.Key : key + ConfigurationPath.KeyDelimiter + item.Key;
                Visit(subKey, item.Value);
            }
        }

        private void Visit(string key, TomlValue value)
        {
            switch (value)
            {
                case TomlTable table:
                    VisitTable(key, table);
                    break;
                case TomlArray array:
                    VisitArray(key, array);
                    break;
                default:
                    _data.Add(key, value.StringValue);
                    break;
            }
        }
    }
}
