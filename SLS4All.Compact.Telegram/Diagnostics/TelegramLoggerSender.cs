// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Channels;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types;

namespace SLS4All.Compact.Diagnostics
{
    public sealed class TelegramLoggerSender : IDisposable
    {
        private readonly TimeSpan _sendDelay;
        private readonly ITelegramBotClient _botClient;
        private readonly ChatId _chatId;
        private readonly Task _outputTask;
        private readonly Channel<string> _messageQueue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleWriter = false,
            SingleReader = true,
            AllowSynchronousContinuations = false,
        });

        public TelegramLoggerSender(ITelegramBotClient botClient, ChatId chatId, TimeSpan sendDelay)
        {
            _botClient = botClient;
            _chatId = chatId;
            _sendDelay = sendDelay;

            _outputTask = Task.Factory.StartNew(
                ProcessLogQueue,
                default,
                TaskCreationOptions.None,
                TaskScheduler.Default).Unwrap();
        }

        public void EnqueueMessage(string message)
        {
            _messageQueue.Writer.TryWrite(message);
        }

        private async Task ProcessLogQueue()
        {
            var messages = new Queue<string>();
            var buf = new StringBuilder();            
            const int maxMessageSize = 4096;
            while (true)
            {
                if (buf.Length == 0)
                {
                    var text = await _messageQueue.Reader.ReadAsync();
                    await Task.Delay(_sendDelay);
                    if (buf.Length + text.Length > maxMessageSize)
                    {
                        messages.Enqueue(buf.ToString());
                        buf.Clear();
                    }
                    buf.Append(text);
                }
                while (_messageQueue.Reader.TryRead(out var text))
                {
                    if (buf.Length != 0)
                        buf.AppendLine();
                    if (buf.Length + text.Length > maxMessageSize)
                    {
                        messages.Enqueue(buf.ToString());
                        buf.Clear();
                    }
                    buf.Append(text);
                }
                try
                {
                    while (true)
                    {
                        while (messages.TryPeek(out var text))
                        {
                            await _botClient.SendTextMessageAsync(_chatId, text, parseMode: Telegram.Bot.Types.Enums.ParseMode.Html);
                            messages.Dequeue();
                        }

                        if (buf.Length == 0)
                            break;
                        messages.Enqueue(buf.ToString());
                        buf.Clear();
                    }
                }
                catch (Exception)
                {
                    // swallow and try again
                    if (buf.Length != 0) // if the buffer is empty, we will wait anyway
                        await Task.Delay(_sendDelay);
                }
            }
        }

        public void Dispose()
        {
            _messageQueue.Writer.Complete();
            try
            {
                _outputTask.Wait(_sendDelay * 2);
            }
            catch (Exception)
            {
                // swallow
            }
        }
    }
}
