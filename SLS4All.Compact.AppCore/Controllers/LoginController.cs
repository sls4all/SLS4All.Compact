// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Camera;
using SLS4All.Compact.Threading;
using SLS4All.Compact.Temperature;
using SkiaSharp;
using Lexical.FileProvider.Package;
using SLS4All.Compact.IO;
using SLS4All.Compact.Graphics;
using Microsoft.AspNetCore.Authorization;
using SLS4All.Compact.Security;
using System.Security.Cryptography;

namespace SLS4All.Compact.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [AllowAnonymous]
    public class LoginController : ControllerBase
    {
        private readonly ISignInManager _signInManager;

        public LoginController(ISignInManager signInManager)
        {
            _signInManager = signInManager;
        }

        [HttpGet("nonce/client")]
        public Task<string> ClientNonce(CancellationToken cancel)
            => _signInManager.GetClientNonce(cancel);

        [HttpGet("nonce/server")]
        public Task<string> ServerNonce(CancellationToken cancel)
            => _signInManager.GetServerNonce(cancel);

        [HttpGet("validate/{nonce}/{hash}")]
        public Task<bool> Validate(string nonce, string hash, CancellationToken cancel)
        {
            return _signInManager.TrySignInUsingHash(nonce, hash, cancel);
        }
    }
}
