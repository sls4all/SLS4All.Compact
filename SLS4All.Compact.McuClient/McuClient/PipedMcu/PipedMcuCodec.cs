// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using SLS4All.Compact.Collections;
using SLS4All.Compact.Printer;
using System.Buffers;
using System.Buffers.Binary;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace SLS4All.Compact.McuClient.PipedMcu
{
    public static class PipedMcuCodec
    {
        [Flags]
        public enum McuCommandFlags : byte
        {
            NotSet = 0,
            IsTimingCritical = 1,
            IsMovement = 2,
        }


        public enum ExceptionType : byte
        {
            Unspecified = 0,
            InvalidOperation,
            McuAutomatedRestart,
        }

        public enum MessageType : byte
        {
            NotSet = 0,
            GetCurrentErrorCommand,
            HasTimingCriticalCommandsScheduleCommand,
            SendCommand,
            SendCancelCommand,
            SendCancelManyCommand,
            TryReplaceCommand,
            SendWaitCommand,
            SendWaitEvent,
            RegisterResponseHandlerCommand,
            UnregisterResponseHandlerCommand,
            ResponseHandlerEvent,
            ExceptionEvent,
            ClockSyncEvent,
            ClockSyncUnreachableEvent,
            ClockSyncExceptionEvent,
            LostCommunicationEvent,
            CollectGarbageCommand,
            EnterPrintingModeCommand,
            ExitPrintingModeCommand,
            MovementCancelCommand,
        }

        public const int MinMessageLength = 5;

        public static (int Length, MessageType Type) DecodeHeader(Span<byte> span)
            => (MemoryMarshal.Read<int>(span), (MessageType)span[4]);

        public static void Initialize(ref Span<byte> span, MessageType value)
        {
            span[4] = (byte)value;
            span = span.Slice(MinMessageLength);
        }

        public static ArraySegment<byte> Finish(byte[] buffer, Span<byte> span)
        {
            var length = (int)Unsafe.ByteOffset(ref buffer[0], ref MemoryMarshal.GetReference(span));
            MemoryMarshal.Write(buffer, length);
            return new ArraySegment<byte>(buffer, 0, length);
        }

        public static Span<byte> Finish(Span<byte> buffer, Span<byte> span)
        {
            var length = (int)Unsafe.ByteOffset(ref MemoryMarshal.GetReference(buffer), ref MemoryMarshal.GetReference(span));
            MemoryMarshal.Write(buffer, length);
            return buffer.Slice(0, length);
        }

        public static byte[] RentBuffer(int size)
            => ArrayPool<byte>.Shared.Rent(MinMessageLength + size);

        public static void ReturnBuffer(byte[] buffer)
            => ArrayPool<byte>.Shared.Return(buffer, false);

        public static int Measure(IReadOnlyList<McuCommand> commands)
        {
            var length = 4;
            for (int i = 0; i < commands.Count; i++)
                length += Measure(commands[i]);
            return length;
        }

        public static int Measure(in McuOccasion value)
            => 8 + 8 + 8;

        public static int Measure(in McuSendResult value)
            => 8 + 4;

        public static int Measure(in McuSendResult? value)
            => value == null ? 1 : 1 + 8 + 4;

        public static void Write(ref Span<byte> span, IReadOnlyList<McuCommand> commands)
        {
            Write(ref span, commands.Count);
            for (int i = 0; i < commands.Count; i++)
                Write(ref span, commands[i]);
        }

        public static int Measure(McuCommand command)
        {
            var length = 1 + 1 + 8 + 8;
            for (int i = 0; i < command.ArgumentCount; i++)
            {
                var buffer = command[i].Buffer;
                if (buffer.Array != null)
                {
                    length += 1 + 1 + buffer.Count;
                }
                else
                {
                    length += 1 + 8;
                }
            }
            return length;
        }

        public static McuCommand ReadCommand(ref Span<byte> span, IMcu? factoryMcu, PipedCommandFactory? factory)
        {
            var commandId = ReadByte(ref span);
            var command = factory?.BorrowCommand(commandId) 
                ?? factoryMcu?.Config.IdToCommand[commandId].Clone() 
                ?? throw new InvalidOperationException("Mcu or factory must be specified");
            try
            {
                var flags = (McuCommandFlags)ReadByte(ref span);
                command.IsTimingCritical = flags.HasFlag(McuCommandFlags.IsTimingCritical);
                command.IsMovement = flags.HasFlag(McuCommandFlags.IsMovement);
                command.SentTimestamp = ReadDouble(ref span);
                command.ReceiveTimestamp = ReadDouble(ref span);
                for (int i = 0; i < command.ArgumentCount; i++)
                {
                    if (ReadBoolean(ref span))
                    {
                        var length = ReadByte(ref span);
                        if (length == 0)
                            command[i] = new ArraySegment<byte>([], 0, 0);
                        else
                        {
                            if (factory != null)
                                command[i] = factory.BorrowBuffer(length);
                            else
                                command[i] = new ArraySegment<byte>(new byte[length], 0, length);
                            Debug.Assert(command[i].Buffer.Count == length);
                            ReadSpan(ref span, command[i].Buffer);
                        }
                    }
                    else
                    {
                        command[i] = ReadInt64(ref span);
                    }
                }
                return command;
            }
            catch
            {
                factory?.ReturnCommand(command);
                throw;
            }
        }

        public static void Write(ref Span<byte> span, McuCommand command)
        {
            var flags = (command.IsTimingCritical ? McuCommandFlags.IsTimingCritical : 0) 
                | (command.IsMovement ? McuCommandFlags.IsMovement : 0);
            Write(ref span, (byte)command.CommandId);
            Write(ref span, (byte)flags);
            Write(ref span, command.SentTimestamp);
            Write(ref span, command.ReceiveTimestamp);
            for (int i = 0; i < command.ArgumentCount; i++)
            {
                var value = command[i];
                if (value.Buffer.Array != null)
                {
                    Write(ref span, true);
                    Write(ref span, (byte)value.Buffer.Count);
                    Write(ref span, value.Buffer);
                }
                else
                {
                    Write(ref span, false);
                    Write(ref span, value.Int64);
                }
            }
        }

        public static McuCommand[] ReadCommands(ref Span<byte> span, IMcu? factoryMcu, PipedCommandFactory? factory)
        {
            var count = ReadInt32(ref span);
            var res = new McuCommand[count];
            for (int i = 0; i < count; i++)
            {
                res[i] = ReadCommand(ref span, factoryMcu, factory);
            }
            return res;
        }

        public static int Measure(Exception ex)
        {
            if (ex is McuAutomatedRestartException restart)
                return 1 + Measure(ex.Message) + 1;
            else
                return 1 + Measure(ex.Message);
        }

        public static byte ReadByte(ref Span<byte> span)
        {
            var value = span[0];
            span = span.Slice(1);
            return value;
        }

        public static Exception ReadException(ref Span<byte> span)
        {
            var type = (ExceptionType)ReadByte(ref span);
            var message = ReadString(ref span);
            switch (type)
            {
                case ExceptionType.Unspecified:
                    return new McuException(message);
                case ExceptionType.InvalidOperation:
                    return new InvalidOperationException(message);
                case ExceptionType.McuAutomatedRestart:
                    return new McuAutomatedRestartException(message, (McuAutomatedRestartReason)ReadByte(ref span));
                default:
                    throw new InvalidOperationException($"Invalid exception type: {type}");
            }
        }

        public static void Write(ref Span<byte> span, Exception ex)
        {
            if (ex is McuAutomatedRestartException restart)
            {
                Write(ref span, (byte)ExceptionType.McuAutomatedRestart);
                Write(ref span, restart.Message);
                Write(ref span, (byte)restart.Reason);
            }
            else if (ex is InvalidOperationException)
            {
                Write(ref span, (byte)ExceptionType.InvalidOperation);
                Write(ref span, ex.Message);
            }
            else
            {
                Write(ref span, (byte)ExceptionType.Unspecified);
                Write(ref span, ex.Message);
            }
        }

        public static int Measure(string str)
            => 4 + Encoding.UTF8.GetByteCount(str);

        public static string ReadString(ref Span<byte> span)
        {
            var length = ReadInt32(ref span);
            var str = Encoding.UTF8.GetString(span.Slice(0, length));
            span = span.Slice(length);
            return str;
        }

        public static void Write(ref Span<byte> span, string str)
        {
            var length = Encoding.UTF8.GetByteCount(str);
            Write(ref span, str.Length);
            var length2 = Encoding.UTF8.GetBytes(str, span);
            Debug.Assert(length == length2);
            span = span.Slice(length);
        }

        public static void Write(ref Span<byte> span, in McuOccasion value)
        {
            Debug.Assert(Marshal.SizeOf<McuOccasion>() == 24);
            MemoryMarshal.Write(span, value);
            span = span.Slice(24);
        }

        public static McuOccasion ReadMcuOccasion(ref Span<byte> span)
        {
            Debug.Assert(Marshal.SizeOf<McuOccasion>() == 24);
            var res = MemoryMarshal.Read<McuOccasion>(span);
            span = span.Slice(24);
            return res;
        }

        public static void Write(ref Span<byte> span, in McuSendResult value)
        {
            Write(ref span, value.Id);
            Write(ref span, value.Index);
        }

        public static void Write(ref Span<byte> span, in McuSendResult? value)
        {
            if (value == null)
                Write(ref span, false);
            else
            {
                Write(ref span, true);
                Write(ref span, value.Value);
            }
        }

        public static void Write(ref Span<byte> span, double value)
        {
            MemoryMarshal.Write(span, value);
            span = span.Slice(8);
        }

        public static double ReadDouble(ref Span<byte> span)
        {
            var value = MemoryMarshal.Read<double>(span);
            span = span.Slice(8);
            return value;
        }

        public static void Write(ref Span<byte> span, Span<byte> value)
        {
            value.CopyTo(span);
            span = span.Slice(value.Length);
        }

        public static void ReadSpan(ref Span<byte> span, Span<byte> value)
        {
            span.Slice(0, value.Length).CopyTo(value);
            span = span.Slice(value.Length);
        }

        public static void Write(ref Span<byte> span, int value)
        {
            MemoryMarshal.Write(span, value);
            span = span.Slice(4);
        }

        public static void Write(ref Span<byte> span, long value)
        {
            MemoryMarshal.Write(span, value);
            span = span.Slice(8);
        }

        public static void Write(ref Span<byte> span, ulong value)
        {
            MemoryMarshal.Write(span, value);
            span = span.Slice(8);
        }

        public static void Write(ref Span<byte> span, bool value)
        {
            span[0] = value ? (byte)1 : (byte)0;
            span = span.Slice(1);
        }

        public static void Write(ref Span<byte> span, byte value)
        {
            span[0] = value;
            span = span.Slice(1);
        }

        public static void Write(ref Span<byte> span, in McuTimestamp value)
        {
            Write(ref span, value.Mcu != null);
            Write(ref span, value.ClockPrecise);
            Write(ref span, value.Precision);
        }

        public static McuTimestamp ReadMcuTimestamp(IMcu mcu, ref Span<byte> span)
            => new McuTimestamp(ReadBoolean(ref span) ? mcu : null, ReadInt64(ref span), ReadInt32(ref span));

        public static int Measure(in McuTimestamp value)
            => 1 + 8 + 4;

        public static McuSendResult ReadMcuSendResult(ref Span<byte> span)
            => new McuSendResult(ReadUInt64(ref span), ReadInt32(ref span));

        public static McuSendResult? ReadMcuSendResultNullable(ref Span<byte> span)
            => ReadBoolean(ref span) ? ReadMcuSendResult(ref span) : null;

        public static bool ReadBoolean(ref Span<byte> span)
        {
            var value = span[0] != 0;
            span = span.Slice(1);
            return value;
        }

        public static int ReadInt32(ref Span<byte> span)
        {
            var value = MemoryMarshal.Read<int>(span);
            span = span.Slice(4);
            return value;
        }

        public static long ReadInt64(ref Span<byte> span)
        {
            var value = MemoryMarshal.Read<long>(span);
            span = span.Slice(8);
            return value;
        }

        public static ulong ReadUInt64(ref Span<byte> span)
        {
            var value = MemoryMarshal.Read<ulong>(span);
            span = span.Slice(8);
            return value;
        }

        public static bool IsClosedPipeException(Exception ex)
            => ex is IOException || ex is ObjectDisposedException;
    }
}
