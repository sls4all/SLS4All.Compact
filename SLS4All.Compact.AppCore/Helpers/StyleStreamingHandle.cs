// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileProvider.Common;
using Microsoft.JSInterop;
using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Helpers
{
    public sealed class ImageStreamingHandle
    {
        private readonly IJSRuntime _jsRuntime;
        private readonly ImageStreamingHelper _helper;
        private readonly string _id;
        private readonly BackgroundTask _streamTask;
        private readonly Func<string> _uriFunc;
        private AsyncEvent<MimeData>? _imageCapturedEvent;

        public ImageStreamingHandle(IJSRuntime jsRuntime, ImageStreamingHelper helper, string id, Func<string> uriFunc)
        {
            _jsRuntime = jsRuntime;
            _helper = helper;
            _id = id;
            _uriFunc = uriFunc;
            _streamTask = new BackgroundTask(noStatus: true);
        }

        private Task RefreshImageSrc(MimeData image, CancellationToken cancel)
        {
            return _streamTask.StartValueTask(null,
                async cancel =>
                {
                    try
                    {
                        await _jsRuntime.InvokeVoidAsync("AppHelpersInvoke", cancel, "setImageSrcByIdIfLoaded", _id, _uriFunc());
                    }
                    catch (Exception)
                    {
                        TryUnregisterImageReady();
                    }
                });
        }

        public bool TryRegisterImageReady()
        {
            if (_imageCapturedEvent == null && _helper.TryGetCapturedEvent(_id, out _imageCapturedEvent))
            {
                _imageCapturedEvent.AddHandler(RefreshImageSrc);
                return true;
            }
            else
                return false;
        }

        public void TryUnregisterImageReady()
        {
            if (_imageCapturedEvent != null)
            {
                _imageCapturedEvent.RemoveHandler(RefreshImageSrc);
                _imageCapturedEvent = null;
            }
        }
    }
}
