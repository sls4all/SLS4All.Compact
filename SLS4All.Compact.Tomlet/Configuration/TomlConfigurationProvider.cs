// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Tomlet.Exceptions;

namespace SLS4All.Compact.Configuration
{
    public class TomlConfigurationProvider : FileConfigurationProvider
    {
        public TomlConfigurationProvider(TomlConfigurationSource source) : base(source) { }

        public override void Load(Stream stream)
        {
            try
            {
                Data = TomlConfigurationFileParser.Parse(stream);
            }
            catch (TomlException e)
            {
                throw new FormatException("TOML parse error", e);
            }
        }
    }
}
