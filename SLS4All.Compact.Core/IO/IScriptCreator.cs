// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using SLS4All.Compact.Helpers;

namespace SLS4All.Compact.IO
{
    public interface IScriptCreator
    {
        IActiveScript CreateScript(IEnumerable<string?>? sources, params (string name, object value)[] parameters);
        IActiveScript CreateScript(string? source, params (string name, object value)[] parameters);
    }
}