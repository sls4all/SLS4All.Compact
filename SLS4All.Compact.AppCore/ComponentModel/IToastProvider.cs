// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using SLS4All.Compact.Threading;
using System.Diagnostics;

namespace SLS4All.Compact.ComponentModel
{
    public enum ToastMessageType
    {
        NotSet = 0,
        Information,
        Warning,
        Error,
    }

    public enum ToastDismissReason
    {
        NotSet = 0,
        UserClosed,
        KeyOverlay,
    }

    public sealed class ToastMessage
    {
        private WeakReference? _keyReference;
        private WeakReference? _ownerReference;

        public object? Key
        {
            get => _keyReference?.Target;
            set
            {
                if (value == null)
                    _keyReference = null;
                else if (_keyReference == null)
                    _keyReference = new WeakReference(value);
                else
                    _keyReference.Target = value;
            }
        }
        public bool HasOnlyForLayoutOwner
            => _ownerReference != null;
        public object? OnlyForLayoutOwner
        {
            get => _ownerReference?.Target;
            set
            {
                if (value == null)
                    _ownerReference = null;
                else if (_ownerReference == null)
                    _ownerReference = new WeakReference(value);
                else
                    _ownerReference.Target = value;
            }
        }
        public Stopwatch Stopwatch { get; } = Stopwatch.StartNew();
        public required ToastMessageType Type { get; set; }
        public RenderFragment? Header { get; set; }
        public string? HeaderText { get; set; }
        public RenderFragment? Body { get; set; }
        public string? BodyText { get; set; }
        public Exception? Exception { get; set; }
        public ToastDismissReason Dismissed { get; internal set; }
        public string? TargetUri { get; set; }
        public bool TargetUriForceReload { get; set; }
        public bool Silent { get; set; } = false;
    }

    public interface IToastProvider
    {
        IEnumerable<ToastMessage> Messages { get; }
        AsyncEvent MessagesChanged { get; }
        void Show(ToastMessage message);
        void Dismiss(ToastMessage message, ToastDismissReason reason);
    }
}