// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Lexical.FileProvider.Package;
using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nito.AsyncEx;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Sensors;
using SLS4All.Compact.Threading;
using System;
using System.Collections;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace SLS4All.Compact.McuClient.Pins
{
    public class McuSdCardSpiOptions
    {
        public required string Bus { get; set; }
        public required string CsPin { get; set; }
        public int InitSpiSpeed { get; set; } = 400_000;
        public int DefaultSpiSpeed { get; set; } = 20_000_000;
    }

    /// <remarks>
    /// This class uses SPI Continuous transfer which will not work correctly if there are other
    /// devices on the same bus and they are accessed internally by the MCU (like thermocouplers, galvos, ...).
    /// This class should be therefore used only if these other devices are not initialized, like for
    /// firmware update case that happens directly after reset.
    /// </remarks>
    public class McuSdCardSpi : IMcuSdCard
    {
        private enum SdCommand : byte
        {
            GO_IDLE_STATE = 0,
            ALL_SEND_CID = 2,
            SET_REL_ADDR = 3,
            SET_BUS_WIDTH = 6,
            SEL_DESEL_CARD = 7,
            SEND_IF_COND = 8,
            SEND_CSD = 9,
            SEND_CID = 10,
            SD_SEND_OP_COND = 41,
            SEND_STATUS = 13,
            SET_BLOCKLEN = 16,
            READ_SINGLE_BLOCK = 17,
            WRITE_BLOCK = 24,
            APP_CMD = 55,
            READ_OCR = 58,
            CRC_ON_OFF = 59,
        }

        private readonly ILogger _logger;
        private readonly IOptions<McuSdCardSpiOptions> _options;
        private readonly string _name;
        private readonly McuSpi _spi;
        private readonly static byte[] _dataFF32 = new byte[32];
        private readonly AsyncLock _lock = new();
        private readonly PrimitiveList<byte> _sectorTemp = new();
        private const int _sectorSize = 512;
        private int _sdVersion;
        private bool _highCapacity;
        private long _totalSectors;
        private bool _isWriteProtected;
        private volatile FrozenDictionary<string, object> _cardInfo = FrozenDictionary<string, object>.Empty;

        public IMcu Mcu => _spi.Mcu;
        public int SectorSize => _sectorSize;
        public long TotalSectors => _totalSectors;
        public bool IsWriteProtected => _isWriteProtected;
        public FrozenDictionary<string, object> CardInfo => _cardInfo;

        public McuSdCardSpi(
            ILogger logger,
            IOptions<McuSdCardSpiOptions> options,
            McuManager manager,
            string name)
        {
            _logger = logger;
            _options = options;
            _name = name;

            var o = options.Value;
            Array.Fill(_dataFF32, (byte)0xFF);
            _spi = new McuSpi(
                manager.ClaimBus(o.Bus, shareType: nameof(InovaGate1TemperatureSensor)),
                manager.ClaimPin(McuPinType.ChipSelect, o.CsPin, shareType: nameof(InovaGate1TemperatureSensor)),
                mode: 0,
                rate: o.InitSpiSpeed);

            manager.RegisterSetup(Mcu, OnSetup);
        }

        private async Task OnSetup(CancellationToken cancel)
        {
            var options = _options.Value;

            // Send reset command (CMD0)
            if (!await CheckCommand(1, SdCommand.GO_IDLE_STATE, 0, cancel: cancel))
                throw new IOException($"Failed to reset SD Card {_name}. Check wheter the SD Card is inserted. Cards cannot be hot-swapped.");
            // Check Voltage Range (CMD8). Only Cards meeting the v2.0 spec
            // support this. V1.0 cards (and MMC) will return illegal command.
            uint checkPattern = 0b1010;
            var response = await SendCommandWithResponse(SdCommand.SEND_IF_COND, (1 << 8) | checkPattern, cancel: cancel);
            response = Strip(response);
            if (response.Count >= 1 && (response[0] & (1 << 2)) != 0) // CMD8 is illegal, this is a version 1.0 card
                _sdVersion = 1;
            else if (response.Count == 5)
            {
                if (response[0] == 1)
                {
                    _sdVersion = 2;
                    if (!(response[^2] == 1 && response[^1] == checkPattern))
                        throw new IOException($"SD Card not running in a compatible voltage range: {_name}");
                }
                else
                    throw new IOException($"CMD8 Error {response[0]:x2}: {_name}");
            }
            else
                throw new IOException($"Invalid CMD8 response {Convert.ToHexString(response)}: {_name}");
            // Enable SD crc checks (CMD59)
            if (!await CheckCommand(1, SdCommand.CRC_ON_OFF, 1, cancel: cancel))
                _logger.LogWarning($"Failed to enable CRC checks: {_name}");
            if (_sdVersion == 2)
            {
                // Init card and come out of idle (ACMD41)
                // Version 2 Cards may init before checking the OCR
                if (!await CheckCommand(0, SdCommand.SD_SEND_OP_COND, 1 << 30, isAppCmd: true, cancel: cancel))
                    throw new IOException($"SD Card did not come out of IDLE after reset: {_name}");
            }
            // Read OCR Register (CMD58)
            response = Strip(await SendCommandWithResponse(SdCommand.READ_OCR, 0, cancel: cancel));
            // If 'READ_OCR' is illegal then this is likely MMC.
            // At this time MMC is not supported
            if (response.Count == 5)
            {
                if (_sdVersion == 1 && response[0] == 1)
                {
                    // Check acceptable volatage range for V1 cards
                    if (response[2] != 0xFF)
                        throw new IOException($"Card does not support 3.3v range: {_name}");
                }
                else if (_sdVersion == 2 && response[0] == 0)
                {
                    // Determine if this is a high capacity sdcard
                    if ((response[1] & 0x40) != 0)
                        _highCapacity = true;
                }
                else
                    throw new IOException($"READ_OCR Error {Convert.ToHexString(response)}: {_name}");
            }
            else
                throw new IOException($"Invalid OCR Response {Convert.ToHexString(response)}: {_name}");
            if (_sdVersion == 1)
            {
                // Init card and come out of idle (ACMD41)
                // Version 1 Cards do this after checking the OCR
                if (!await CheckCommand(0, SdCommand.SD_SEND_OP_COND, 0, isAppCmd: true, cancel: cancel))
                    throw new IOException($"SD Card did not come out of IDLE after reset: {_name}");
            }
            // Set block size to 512 (CMD16)
            if (!await CheckCommand(0, SdCommand.SET_BLOCKLEN, _sectorSize, tries: 5))
                throw new IOException($"Failed to set block size: {_name}");
            // Read out CSD and CID information registers
            await ProcessCidReg(cancel);
            await ProcessCsdReg(cancel);
            await _spi.SetRate(options.DefaultSpiSpeed, cancel);
        }

        public async Task ReadSectors(uint startSector, int count, Stream stream, CancellationToken cancel = default)
        {
            using (await _lock.LockAsync(cancel))
            {
                for (int i = 0; i < count; i++)
                {
                    var sector = startSector + i;
                    var offset = (uint)(!_highCapacity ? sector * _sectorSize : sector);
                    ArraySegment<byte> buffer = default;
                    await _spi.Continuous(async cancel =>
                    {
                        SendCommand(SdCommand.READ_SINGLE_BLOCK, offset);
                        buffer = await DoBlockRead(buffer: _sectorTemp, size: _sectorSize, cancel: cancel);
                    }, cancel);
                    if (buffer == default)
                        throw new IOException($"Failed to read sector {sector}: {_name}");
                    await stream.WriteAsync(buffer, cancel);
                }
            }
        }

        public async Task WriteSectors(uint startSector, int count, Stream stream, CancellationToken cancel = default)
        {
            using (await _lock.LockAsync(cancel))
            {
                StringBuilder? error = null;
                for (int i = 0; i < count; i++)
                {
                    var sector = startSector + i;
                    var offset = (uint)(!_highCapacity ? sector * _sectorSize : sector);
                    await _spi.Continuous(async cancel =>
                    {
                        SendCommand(SdCommand.WRITE_BLOCK, offset);
                        var sdResp = await FindSdResponse(cancel: cancel);
                            if (sdResp == 0xFF)
                            throw new IOException($"Invalid write block response {sdResp:x1}: {_name}");
                        _sectorTemp.Count = _sectorSize + 1;
                        _sectorTemp[0] = 0xFE;
                        await stream.ReadExactlyAsync(_sectorTemp.Memory.Slice(1, _sectorSize), cancel);
                        var crc = CalcCrc16(_sectorTemp.Span.Slice(1, _sectorSize));
                        _sectorTemp.Add((byte)(crc >> 8));
                        _sectorTemp.Add((byte)(crc));
                        var toSend = _sectorTemp.Segment;
                        while (toSend.Count > 0)
                        {
                            var size = Math.Min(32, toSend.Count);
                            _spi.Send(toSend.Slice(0, size), McuCommandPriority.Default, McuOccasion.Now);
                            toSend = toSend.Slice(size);
                        }

                        var resp = await FindSdResponse(cancel: cancel);
                        if ((resp & 0x1F) == 0x05)
                        {
                            if (!await WaitForWrite(cancel: cancel))
                                (error ??= new()).AppendFormat($"Failed to wait for writing to finish. ");
                            var status = Strip(await SendCommandWithResponse(SdCommand.SEND_STATUS, 0, cancel: cancel));
                            if (status.Count >= 2)
                            {
                                if (status[1] != 0)
                                    (error ??= new()).AppendFormat($"Write error {status[1]:x2}. ");
                            }
                            else
                                (error ??= new()).AppendFormat($"Invalid status response after write {Convert.ToHexString(status)}. ");
                        }
                        else
                            (error ??= new()).AppendFormat($"Write error {resp:x2}. ");
                    }, cancel);

                    if (error != null)
                    {
                        error.AppendFormat($" Name: {_name}");
                        throw new IOException(error.ToString());
                    }
                }
            }
        }

        private async Task<byte> FindSdResponse(int tries = 10, CancellationToken cancel = default)
        {
            while (tries > 0)
            {
                var resp = await _spi.Transfer(new ArraySegment<byte>(_dataFF32, 0, 1), McuCommandPriority.Default, McuOccasion.Now, cancel: cancel);
                if (resp.Count == 1 && resp[0] != 0xFF)
                    return resp[0];
                tries--;
            }
            return 0xFF;
        }

        private async Task<bool> WaitForWrite(int tries = 3907, CancellationToken cancel = default)
        {
            while (tries > 0)
            {
                var resp = await _spi.Transfer(new ArraySegment<byte>(_dataFF32, 0, 1), McuCommandPriority.Default, McuOccasion.Now, cancel: cancel);
                if (resp.Count == 1 && resp[0] != 0x00)
                    return true;
                tries--;
            }
            return false;
        }

        private async Task<bool> FindSdToken(int token, int tries = 10, CancellationToken cancel = default)
        {
            while (tries > 0)
            {
                var resp = await _spi.Transfer(new ArraySegment<byte>(_dataFF32, 0, 1), McuCommandPriority.Default, McuOccasion.Now, cancel: cancel);
                if (resp.Count == 1 && resp[0] == token)
                    return true;
                tries--;
            }
            return false;
        }

        private async Task<ArraySegment<byte>> DoBlockRead(PrimitiveList<byte>? buffer = null, int size = _sectorSize, bool guessPoly = false, CancellationToken cancel = default)
        {
            var validResponse = true;
            var sdResp = await FindSdResponse(cancel: cancel);
            if (sdResp != 0)
            {
                _logger.LogError($"Invalid read block response {sdResp:x1}: {_name}");
                validResponse = false;
            }
            if (!await FindSdToken(0xFE, cancel: cancel))
            {
                _logger.LogError($"Read error, unable to find start token: {_name}");
                validResponse = false;
            }
            var readSize = size + 2 /* CRC */;
            if (!validResponse)
            {
                // In the event of an invalid response we will still
                // send 514 bytes to be sure that the sdcard's output
                // buffer is clear
                while (readSize > 0)
                {
                    var count = Math.Min(32, readSize);
                    _spi.Send(new ArraySegment<byte>(_dataFF32, 0, count), McuCommandPriority.Default, McuOccasion.Now);
                    readSize -= count;
                }
                return default;
            }
            buffer ??= new PrimitiveList<byte>();
            buffer.Clear();
            while (readSize > 0)
            {
                var count = Math.Min(32, readSize);
                var read = await _spi.Transfer(new ArraySegment<byte>(_dataFF32, 0, count), McuCommandPriority.Default, McuOccasion.Now, cancel: cancel);
                buffer.AddRange(read);
                readSize -= count;
            }
            var innerData = buffer.Segment.Slice(0, size);
            var crcInt = (buffer[^2] << 8) | buffer[^1];
            var calculatedCrc = CalcCrc16(innerData);
            if (calculatedCrc != crcInt)
            {
                _logger.LogError($"CRC Mismatch, Received: {crcInt:x2}, Calculated: {calculatedCrc:x2}: {_name}");
                return default;
            }
            return innerData;
        }

        private Task ProcessCidReg(CancellationToken cancel)
            => _spi.Continuous(async cancel =>
            {
                SendCommand(SdCommand.SEND_CID, 0);
                var reg = await DoBlockRead(size: 16, cancel: cancel);
                if (reg == default)
                    throw new IOException($"Error reading CID register: {_name}");
                var cid = new Dictionary<string, object>
                {
                    { "manufacturer_id", reg[0] },
                    { "oem_id", Encoding.ASCII.GetString(reg.Slice(1, 2)) },
                    { "product_name", Encoding.ASCII.GetString(reg.Slice(3, 5)) },
                    { "product_revision", $"{reg[8] >> 4}.{reg[8] >> 4}" },
                    { "serial_number", Convert.ToHexString(reg.Slice(9, 4)) },
                    { "manufacturing_date", $"{(((reg[13] & 0xF) << 4) | ((reg[14] >> 4) & 0xF)) + 2000}/{reg[14] & 0xF}" }
                };
                var crc = CalcCrc7(reg.Slice(0, 15));
                if (crc != reg[15])
                    throw new IOException($"CID crc mismatch: {crc:x2}, recd: {reg[15]:x2}: {_name}");
                _cardInfo = cid.ToFrozenDictionary();
            }, cancel);

        private Task ProcessCsdReg(CancellationToken cancel)
            => _spi.Continuous(async cancel =>
            {
                SendCommand(SdCommand.SEND_CSD, 0);
                var reg = await DoBlockRead(size: 16, cancel: cancel);
                if (reg == default)
                    throw new IOException($"Error reading CSD register: {_name}");
                var maxCapacity = 0L;
                var csdType = (reg[0] >> 6) & 0x3;
                if (csdType == 0)
                {
                    // Standard Capacity (CSD Version 1.0)
                    var max_block_len = 1L << (reg[5] & 0xF);
                    var c_size = ((reg[6] & 0x3) << 10) | (reg[7] << 2) | ((reg[8] >> 6) & 0x3);
                    var c_mult = 1L << ((((reg[9] & 0x3) << 1) | (reg[10] >> 7)) + 2);
                    maxCapacity = (c_size + 1) * c_mult * max_block_len;
                }
                else if (csdType == 1)
                {
                    // High Capacity (CSD Version 2.0)
                    var c_size = ((reg[7] & 0x3F) << 16) | (reg[8] << 8) | reg[9];
                    maxCapacity = (c_size + 1) * 512 * 1024;
                }
                else
                    _logger.LogInformation($"Unsupported csd type: {csdType}: {_name}");
                _isWriteProtected = (reg[14] & 0x30) != 0;
                var crc = CalcCrc7(reg.Slice(0, 15));
                if (crc != reg[15])
                    throw new IOException($"CSD crc mismatch: {crc:x2}, recd: {reg[15]:x2}: {_name}");
                var cid = _cardInfo.ToDictionary();
                cid["capacity"] = maxCapacity;
                _cardInfo = cid.ToFrozenDictionary();
                _totalSectors = maxCapacity / _sectorSize;
            }, cancel);

        private static ushort CalcCrc16(Span<byte> data, uint poly = 0x1021)
        {
            uint crc = 0;
            foreach (uint b in data)
            {
                crc ^= b << 8;
                for (var i = 0; i < 8; i++)
                    crc = (crc & 0x8000) != 0 ? (crc << 1) ^ poly : crc << 1;
            }
            return (ushort)crc;
        }

        private static byte CalcCrc7(Span<byte> data, bool withPadding = true, uint poly = 0b10001001 << 1)
        {
            // G(x) = x^7 + x^3 + 1
            // Shift left as we are only calculating a 7 bit CRC
            uint crc = 0;
            foreach (uint b in data)
            {
                crc ^= b;
                for (var i = 0; i < 8; i++)
                    crc = (crc & 0x80) != 0 ? (crc << 1) ^ poly : crc << 1;
            }
            // The sdcard protocol likes the crc left justfied with a
            // padded bit
            if (withPadding)
                crc |= 1;
            return (byte)crc;
        }

        private static ArraySegment<byte> Strip(ArraySegment<byte> segment)
        {
            if (segment.Array == null)
                return default;
            var span = segment.AsSpan();
            int s, e;
            for (s = 0; s < span.Length; s++)
                if (span[s] != 0xFF)
                    break;
            for (e = span.Length - 1; e >= s; e--)
                if (span[e] != 0xFF)
                    break;
            return new ArraySegment<byte>(segment.Array, segment.Offset + s, e - s + 1);
        }

        private void SendCommand(SdCommand cmd, uint arg)
        {
            var request = new PrimitiveList<byte>();
            request.Add() = (byte)((byte)cmd | 0x40);
            for (int i = 3; i >= 0; i--)
                request.Add() = (byte)(arg >> (8 * i));
            request.Add(CalcCrc7(request.Span));
            _spi.Send(request.ToArray(), McuCommandPriority.Default, McuOccasion.Now);
        }

        private Task<ArraySegment<byte>> SendCommandWithResponse(SdCommand cmd, uint arg, CancellationToken cancel = default)
        {
            SendCommand(cmd, arg);
            return _spi.Transfer(new ArraySegment<byte>(_dataFF32, 0, 8), McuCommandPriority.Default, McuOccasion.Now, cancel);
        }

        private async Task<ArraySegment<byte>> SendAppCommandWithResponse(SdCommand cmd, uint arg, CancellationToken cancel = default)
        {
            // CMD55 tells the SD Card that the next command is an
            // Application Specific Command.
            await SendCommandWithResponse(SdCommand.APP_CMD, 0, cancel: cancel);
            return await SendCommandWithResponse(cmd, arg, cancel: cancel);
        }

        private async Task<bool> CheckCommand(int expected, SdCommand cmd, uint arg, bool isAppCmd = false, int tries = 15, CancellationToken cancel = default)
        {
            while (true)
            {
                cancel.ThrowIfCancellationRequested();
                ArraySegment<byte> response;
                if (isAppCmd)
                    response = await SendAppCommandWithResponse(cmd, arg, cancel: cancel);
                else
                    response = await SendCommandWithResponse(cmd, arg, cancel: cancel);
                response = Strip(response);
                if (response.Count >= 1 && response[0] == expected)
                    return true;
                if (tries-- <= 0)
                    return false;
                await Task.Delay(100, cancel);
            }
        }

        public override string ToString()
            => $"{_name} [SdCardSpi]";
    }
}
