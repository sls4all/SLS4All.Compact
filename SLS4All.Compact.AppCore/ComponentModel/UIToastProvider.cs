// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualBasic;
using Nito.AsyncEx;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Power;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
#pragma warning disable BL0006 // Do not use RenderTree types

namespace SLS4All.Compact.ComponentModel
{
    public class UIToastProviderOptions
    {
        public bool PlayMelody { get; set; } = false;
        public bool RemindInformation { get; set; }
        public bool RemindWarning { get; set; } = false;
        public bool RemindError { get; set; } = false;
        public TimeSpan RemindInterval { get; set; } = TimeSpan.FromMinutes(1);
    }

    public sealed class UIToastProvider : IToastProvider
    {
        private readonly ILogger<UIToastProvider> _logger;
        private readonly ILogger<ToastMessage> _messageLogger;
        private readonly IOptionsMonitor<UIToastProviderOptions> _options;
        private readonly IObjectFactory<IMelodyClient, object> _melodyClient;
        private readonly ConcurrentDictionary<ToastMessage, bool> _messages;
        private readonly TaskQueue _taskQueue;
        private readonly Lock _melodyLock = new();
        private CancellationTokenSource? _melodyCancel;

        public IEnumerable<ToastMessage> Messages => _messages.Keys;
        public AsyncEvent MessagesChanged { get; } = new();

        public UIToastProvider(
            ILogger<UIToastProvider> logger,
            ILogger<ToastMessage> messageLogger,
            IOptionsMonitor<UIToastProviderOptions> options,
            IObjectFactory<IMelodyClient, object> melodyClient)
        {
            _logger = logger;
            _messageLogger = messageLogger;
            _options = options;
            _melodyClient = melodyClient;
            _taskQueue = new TaskQueue();
            _messages = new();
        }

        public void Show(ToastMessage message)
        {
            var key = message.Key;
            if (key != null)
            {
                foreach (var existing in _messages.Keys)
                {
                    if (Equals(existing.Key, key))
                        Dismiss(existing, ToastDismissReason.KeyOverlay);
                }
            }
            message.Dismissed = ToastDismissReason.NotSet;
            _messages[message] = true;
            var typeLog = message.Type switch
            {
                ToastMessageType.Warning => LogLevel.Warning,
                ToastMessageType.Error => LogLevel.Error,
                _ => LogLevel.Information,
            };
            if (_messageLogger.IsEnabled(typeLog))
            {
                var headerLog = message.HeaderText != null ? message.HeaderText : FormatForLog(message.Header);
                var bodyLog = message.BodyText != null ? message.BodyText : FormatForLog(message.Body);
                _messageLogger.Log(
                    typeLog,
                    message.Exception,
                    $"{headerLog}: {bodyLog}");
            }
            OnMessagesChanged();
            if (!message.Silent)
                PlayMelody();
        }

        private void PlayMelody()
        {
            lock (_melodyLock)
            {
                _melodyCancel?.Cancel();
                _melodyCancel = new CancellationTokenSource();
                var cancel = _melodyCancel.Token;
                Task.Run(async () =>
                {
                    try
                    {
                        var options = _options.CurrentValue;
                        var timer = new PeriodicForceTimer(options.RemindInterval);
                        for (int repeat = 0; ;  repeat++)
                        {
                            var type = _messages.Keys.Where(x => !x.Silent).Select(x => x.Type).DefaultIfEmpty(ToastMessageType.NotSet).Max();
                            Melody melody;
                            switch (type)
                            {
                                case ToastMessageType.Information:
                                    melody = Melody.Information;
                                    if (repeat > 0 && !options.RemindInformation)
                                        return;
                                    break;
                                case ToastMessageType.Warning:
                                    melody = Melody.Warning;
                                    if (repeat > 0 && !options.RemindWarning)
                                        return;
                                    break;
                                case ToastMessageType.Error:
                                    melody = Melody.Error;
                                    if (repeat > 0 && !options.RemindError)
                                        return;
                                    break;
                                default:
                                    return;
                            }
                            using (var melodyClient = _melodyClient.CreateDisposable())
                                await melodyClient.Instance.Play(melody, cancel);
                            if (options.RemindInterval <= TimeSpan.Zero || !options.RemindWarning)
                                break;
                            await timer.WaitForNextTickAsync(cancel);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!cancel.IsCancellationRequested)
                            _logger.LogError(ex, $"Failed to play melody");
                    }
                });
            }
        }

        private static string FormatForLog(RenderFragment? header)
        {
            var buf = new StringBuilder();
            var renderBuilder = new RenderTreeBuilder();
            header?.Invoke(renderBuilder);
            var frames = renderBuilder.GetFrames();
            for (int i = 0; i < frames.Count; i++)
            {
                ref var frame = ref frames.Array[i];
                if (frame.FrameType == RenderTreeFrameType.Text)
                    buf.Append(frame.TextContent);
            }
            return buf.ToString();
        }

        public void Dismiss(ToastMessage message, ToastDismissReason reason)
        {
            message.Dismissed = reason;
            _messages.Remove(message, out _);
            OnMessagesChanged();
        }

        private void OnMessagesChanged()
        {
            _taskQueue.Enqueue(async () =>
            {
                try
                {
                    await MessagesChanged.Invoke(default);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Failed to invoke MessagesChanged");
                }
            }, _logger);
        }
    }
}
