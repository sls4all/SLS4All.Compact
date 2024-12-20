// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Printer;
using SLS4All.Compact.Threading;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Xml.Linq;

namespace SLS4All.Compact.McuClient
{
    public interface IMcu
    {
        McuManager Manager { get; }
        IMcuClockSync ClockSync { get; }
        string Name { get; }
        McuConfig Config { get; }
        bool IsShutdown { get; }
        string? ShutdownReason { get; }
        bool HasLostCommunication { get; }
        bool IsUpdatingFirmware { get; }
        Exception? CurrentError { get; }
        bool IsFake { get; }

        AsyncEvent PreUpdatingEvent { get; }
        AsyncEvent BeforeClockSyncEvent { get; }
        AsyncEvent AfterReadyEvent { get; }
        bool HasTimingCriticalCommandsScheduled { get; }

        Task Run(CancellationToken cancel);
        AsyncLock GetLock(McuBusKey key);
        bool TryLookupCommand(int commandId, [MaybeNullWhen(false)] out McuCommand command);
        bool TryLookupCommand(string commandFormat, [MaybeNullWhen(false)] out McuCommand command);
        IDisposable RegisterResponseHandler(McuCommand? request, McuCommand response, Func<McuCommand, bool>? responseFilter, out Task<McuCommand> task, CancellationToken cancel);
        IDisposable RegisterResponseHandler(McuCommand? request, McuCommand response, McuResponseHandler handler);
        bool TryReplace(McuSendResult id, McuOccasion occasion, McuCommand command);
        McuSendResult Send(McuCommand command, int priority, McuOccasion clock, McuSendResult? cancelFirst = default);
        Task SendWait(McuCommand command, int priority, McuOccasion clock, CancellationToken cancel = default);
        Task<McuCommand> SendWithResponse(McuCommand request, McuCommand response, Func<McuCommand, bool>? responseFilter, int priority, McuOccasion clock, TimeSpan? timeout = default, CancellationToken cancel = default);
        void SendCancel(McuSendResult sendId);
        void SendCancel(IEnumerable<McuSendResult> sendIds);
        McuTimestamp MovementCancel();
        void RegisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler);
        void UnregisterConfigCommand(Func<McuConfigCommands, CancellationToken, ValueTask> handler);
    }

    public static class McuExtensions
    {
        public static McuCommand LookupCommand(this IMcu mcu, int commandId)
        {
            if (mcu.TryLookupCommand(commandId, out var command))
                return command;
            else
                throw new McuException($"Command '{commandId}' was not found on MCU '{mcu.Name}'");
        }

        public static McuCommand LookupCommand(this IMcu mcu, string commandFormat)
        {
            if (mcu.TryLookupCommand(commandFormat, out var command))
                return command;
            else
                throw new McuException($"Command '{commandFormat}' was not found on MCU '{mcu.Name}'");
        }

        public static (McuCommand, McuCommandArgument) LookupCommand(this IMcu mcu, string commandFormat, string param1)
        {
            var cmd = mcu.LookupCommand(commandFormat);
            return (cmd, cmd.GetArgument(param1));
        }

        public static (McuCommand, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument) cmd, string name, long value)
        {
            cmd.Item1.Bind(name, value);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument) cmd, params long[] values)
        {
            cmd.Item1.Bind(values);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument) LookupCommand(this IMcu mcu, string commandFormat, string param1, string param2)
        {
            var cmd = mcu.LookupCommand(commandFormat);
            return (cmd, cmd.GetArgument(param1), cmd.GetArgument(param2));
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument) cmd, string name, long value)
        {
            cmd.Item1.Bind(name, value);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument) cmd, params long[] values)
        {
            cmd.Item1.Bind(values);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument) LookupCommand(this IMcu mcu, string commandFormat, string param1, string param2, string param3)
        {
            var cmd = mcu.LookupCommand(commandFormat);
            return (cmd, cmd.GetArgument(param1), cmd.GetArgument(param2), cmd.GetArgument(param3));
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument) cmd, string name, long value)
        {
            cmd.Item1.Bind(name, value);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument) cmd, params long[] values)
        {
            cmd.Item1.Bind(values);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) LookupCommand(this IMcu mcu, string commandFormat, string param1, string param2, string param3, string param4)
        {
            var cmd = mcu.LookupCommand(commandFormat);
            return (cmd, cmd.GetArgument(param1), cmd.GetArgument(param2), cmd.GetArgument(param3), cmd.GetArgument(param4));
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) cmd, string name, long value)
        {
            cmd.Item1.Bind(name, value);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) cmd, params long[] values)
        {
            cmd.Item1.Bind(values);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) LookupCommand(this IMcu mcu, string commandFormat, string param1, string param2, string param3, string param4, string param5)
        {
            var cmd = mcu.LookupCommand(commandFormat);
            return (cmd, cmd.GetArgument(param1), cmd.GetArgument(param2), cmd.GetArgument(param3), cmd.GetArgument(param4), cmd.GetArgument(param5));
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) cmd, string name, long value)
        {
            cmd.Item1.Bind(name, value);
            return cmd;
        }

        public static (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) Bind(this (McuCommand, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument, McuCommandArgument) cmd, params long[] values)
        {
            cmd.Item1.Bind(values);
            return cmd;
        }

        public static IMcuOutputPin SetupPin(this IMcu mcu, string name, McuPinDescription desc)
        {
            switch (desc.Type)
            {
                case McuPinType.Digital:
                    return new McuDigitalPin(name, desc);
                case McuPinType.Dimmer:
                    return new McuDimmerPin(name, desc);
                case McuPinType.SoftPwm:
                    return new McuSoftPwmPin(name, desc);
                case McuPinType.HardPwm:
                    return new McuHardPwmPin(name, desc);
                default:
                    throw new ArgumentException($"Invalid pin description: {desc}");
            }
        }
    }
}