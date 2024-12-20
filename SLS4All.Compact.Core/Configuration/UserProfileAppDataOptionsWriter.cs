// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace SLS4All.Compact.Configuration
{
    public sealed class UserProfileAppDataOptionsWriter<T> : OptionsWriterBase<T>
    {
        private readonly ILogger<UserProfileAppDataOptionsWriter<T>> _logger;
        private readonly IOptions<UserProfileAppDataWriterOptions> _options;
        private readonly IOptionsMonitor<T> _writtenOptions;
        private readonly AsyncLock _lock;
        private static readonly JsonSerializerOptions s_serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = false,
            AllowTrailingCommas = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            IgnoreReadOnlyProperties = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };
        private static readonly JsonWriterOptions s_writerOptions = new JsonWriterOptions
        {
            Indented = false,
        };

        public UserProfileAppDataOptionsWriter(
            ILogger<UserProfileAppDataOptionsWriter<T>> logger,
            IOptions<UserProfileAppDataWriterOptions> options,
            IOptionsMonitor<T> writtenOptions)
            : base(writtenOptions)
        {
            _logger = logger;
            _options = options;
            _writtenOptions = writtenOptions;
            _lock = new();
        }

        public override async Task Write(T newValue, CancellationToken cancel)
        {
            var options = _options.Value;
            var filename = UserProfileAppDataWriter.GetPrivateOptionsFilename(options.BasePath, typeof(T));
            var tempFilename = filename + ".tmp";
            var newValueString = JsonSerializer.Serialize(newValue, s_serializerOptions);
            _logger.LogDebug($"Saving options to {filename}");
            using (await _lock.LockAsync(cancel))
            {
                for (int t1 = 0; ; t1++)
                {
                    if (t1 >= 50)
                        throw new IOException("Failed to load saved settings");
                    var completionSource = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                    using (cancel.Register(() => completionSource.TrySetCanceled(cancel)))
                    {
                        using (_writtenOptions.OnChange(current =>
                        {
                            var currentValueString = JsonSerializer.Serialize(current, s_serializerOptions);
                            completionSource.TrySetResult(currentValueString == newValueString);
                        }))
                        {
                            Directory.CreateDirectory(Path.GetDirectoryName(tempFilename)!);
                            using (var stream = File.Open(tempFilename, FileMode.Create, FileAccess.Write))
                            using (var writer = new Utf8JsonWriter(stream, s_writerOptions))
                            {
                                writer.WriteStartObject();
                                writer.WritePropertyName(UserProfileAppDataWriter.GetOptionsSectionName(typeof(T)));
                                writer.WriteRawValue(newValueString);
                                writer.WriteEndObject();
                            }
                            for (int t2 = 0; ; t2++)
                            {
                                try
                                {
                                    File.Move(tempFilename, filename, true);
                                    break;
                                }
                                catch (IOException) when (t2 < 50)
                                {
                                    // possibly sharing error, delay
                                    await Task.Delay(100);
                                }
                            }
                            if (await completionSource.Task)
                            {
                                _logger.LogDebug($"Confirmed saved options in {filename}");
                                break;
                            }
                            else
                            {
                                _logger.LogDebug($"Reloaded options in {filename} are not the saved ones, will try again");
                                await Task.Delay(100);
                            }
                        }
                    }
                }
            }
        }

        public override bool Equals(T x, T y)
        {
            if (ReferenceEquals(x, y))
                return true;
            var strx = JsonSerializer.Serialize(x, s_serializerOptions);
            var stry = JsonSerializer.Serialize(y, s_serializerOptions);
            return strx == stry;
        }

        public override T Clone(T obj)
        {
            var str = JsonSerializer.Serialize(obj, s_serializerOptions);
            var clone = JsonSerializer.Deserialize<T>(str, s_serializerOptions)!;
            return clone;
        }
    }
}
