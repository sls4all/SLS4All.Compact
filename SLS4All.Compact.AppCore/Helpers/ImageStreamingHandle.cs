// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileProvider.Common;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.JSInterop;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public sealed class StyleStreamingHandle<T>
        where T : struct, ITuple
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly StyleStreamingHelperBase<T> _helper;
        private readonly string _id;
        private readonly string[] _keys;
        private readonly T _defaultValue;
        private readonly BackgroundTask _streamTask;
        private AsyncEvent<T>? _styleCapturedEvent;

        public StyleStreamingHandle(IJSRuntime jsRuntime, StyleStreamingHelperBase<T> helper, string id, string[] keys, T defaultValue = default)
        {
            _jsRuntime = jsRuntime;
            _helper = helper;
            _id = id;
            _keys = keys;
            _defaultValue = defaultValue;
            _streamTask = new BackgroundTask(noStatus: true);
        }

        private Task RefreshStyle(T style, CancellationToken cancel)
        {
            return _streamTask.StartValueTask(null,
                async cancel =>
                {
                    try
                    {
                        switch (style.Length)
                        {
                            case 1:
                                await _jsRuntime.InvokeVoidAsync("AppHelpersInvoke", cancel, "setStyle1ById", _id, _keys[0], style[0] ?? _defaultValue[0]);
                                break;
                            case 2:
                                await _jsRuntime.InvokeVoidAsync("AppHelpersInvoke", cancel, "setStyle2ById", _id, _keys[0], style[0] ?? _defaultValue[0], _keys[1], style[1] ?? _defaultValue[1]);
                                break;
                            case 3:
                                await _jsRuntime.InvokeVoidAsync("AppHelpersInvoke", cancel, "setStyle3ById", _id, _keys[0], style[0] ?? _defaultValue[0], _keys[1], style[1] ?? _defaultValue[1], _keys[2], style[2] ?? _defaultValue[2]);
                                break;
                            case 4:
                                await _jsRuntime.InvokeVoidAsync("AppHelpersInvoke", cancel, "setStyle4ById", _id, _keys[0], style[0] ?? _defaultValue[0], _keys[1], style[1] ?? _defaultValue[1], _keys[2], style[2] ?? _defaultValue[2], _keys[3], style[3] ?? _defaultValue[3]);
                                break;
                            default:
                                throw new ArgumentException("Invalid number of styles");
                        }
                    }
                    catch (Exception)
                    {
                        TryUnregisterStyleReady();
                    }
                });
        }

        public bool TryRegisterStyleReady()
        {
            if (_styleCapturedEvent == null && _helper.TryGetCapturedEvent(_id, out _styleCapturedEvent))
            {
                _styleCapturedEvent.AddHandler(RefreshStyle);
                return true;
            }
            else
                return false;
        }

        public void TryUnregisterStyleReady()
        {
            if (_styleCapturedEvent != null)
            {
                _styleCapturedEvent.RemoveHandler(RefreshStyle);
                _styleCapturedEvent = null;
            }
        }
    }
}
