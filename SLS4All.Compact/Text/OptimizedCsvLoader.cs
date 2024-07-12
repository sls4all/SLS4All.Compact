// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Text
{
    public sealed class OptimizedCsvLoader
    {
        private readonly CultureInfo _culture;
        private readonly List<sbyte> _values8 = new();
        private readonly List<int> _values32 = new();
        private readonly List<long> _values64 = new();
        private readonly List<string> _strings = new();
        private readonly Dictionary<string, uint> _stringDict = new();
        private readonly List<uint> _columns = new();
        private readonly List<(int start, int count)> _lines = new();
        private const int _maxTypes = 6;
        private const uint _maxValuesPerType = uint.MaxValue / _maxTypes;
        private const uint _sbyteIndex = _maxValuesPerType * 0;
        private const uint _intIndex = _maxValuesPerType * 1;
        private const uint _longIndex = _maxValuesPerType * 2;
        private const uint _floatIndex = _maxValuesPerType * 3;
        private const uint _doubleIndex = _maxValuesPerType * 4;
        private const uint _stringIndex = _maxValuesPerType * 5;
        private const uint _emptyStringIndex = _stringIndex + 0;

        public int RowCount => _lines.Count;

        public OptimizedCsvLoader(CultureInfo culture)
        {
            _culture = culture;
            _strings.Add("");
        }

        public int GetInt32(int row, int column)
        {
            var line = _lines[row];
            if (column < 0 || column >= line.count)
                throw new ArgumentOutOfRangeException(nameof(column));
            var item = _columns[line.start + column];
            if (item < _intIndex)
                return _values8[(int)(item - _sbyteIndex)];
            else if (item < _longIndex)
                return _values32[(int)(item - _intIndex)];
            else
                throw new FormatException("Not a Int32");
        }

        public long GetInt64(int row, int column)
        {
            var line = _lines[row];
            if (column < 0 || column >= line.count)
                throw new ArgumentOutOfRangeException(nameof(column));
            var item = _columns[line.start + column];
            if (item < _intIndex)
                return _values8[(int)(item - _sbyteIndex)];
            else if (item < _longIndex)
                return _values32[(int)(item - _intIndex)];
            else if (item < _floatIndex)
                return _values64[(int)(item - _longIndex)];
            else
                throw new FormatException("Not a Int64");
        }

        public float GetSingle(int row, int column)
        {
            var line = _lines[row];
            if (column < 0 || column >= line.count)
                throw new ArgumentOutOfRangeException(nameof(column));
            var item = _columns[line.start + column];
            if (item >= _floatIndex && item < _doubleIndex) // optimized case
            {
                var value = _values32[(int)(item - _floatIndex)];
                return Unsafe.As<int, float>(ref value);
            }
            if (item >= _doubleIndex && item < _stringIndex) // optimized case
            {
                var value = _values64[(int)(item - _doubleIndex)];
                return (float)Unsafe.As<long, double>(ref value);
            }
            else if (item < _intIndex)
                return _values8[(int)(item - _sbyteIndex)];
            else if (item < _longIndex)
                return _values32[(int)(item - _intIndex)];
            else if (item < _doubleIndex)
                return _values64[(int)(item - _longIndex)];
            else
                throw new FormatException("Not a Double");
        }

        public double GetDouble(int row, int column)
        {
            var line = _lines[row];
            if (column < 0 || column >= line.count)
                throw new ArgumentOutOfRangeException(nameof(column));
            var item = _columns[line.start + column];
            if (item >= _floatIndex && item < _doubleIndex) // optimized case
            {
                var value = _values32[(int)(item - _floatIndex)];
                return Unsafe.As<int, float>(ref value);
            }
            if (item >= _doubleIndex && item < _stringIndex) // optimized case
            {
                var value = _values64[(int)(item - _doubleIndex)];
                return Unsafe.As<long, double>(ref value);
            }
            else if (item < _intIndex)
                return _values8[(int)(item - _sbyteIndex)];
            else if (item < _longIndex)
                return _values32[(int)(item - _intIndex)];
            else if (item < _doubleIndex)
                return _values64[(int)(item - _longIndex)];
            else
                throw new FormatException("Not a Double");
        }

        public string GetString(int row, int column)
        {
            var line = _lines[row];
            if (column < 0 || column >= line.count)
                throw new ArgumentOutOfRangeException(nameof(column));
            var item = _columns[line.start + column];
            if (item >= _stringIndex) // optimized case
                return _strings[(int)(item - _stringIndex)];
            else if (item < _intIndex)
                return _values8[(int)(item - _sbyteIndex)].ToString(_culture);
            else if (item < _longIndex)
                return _values32[(int)(item - _intIndex)].ToString(_culture);
            else if (item < _floatIndex)
                return _values64[(int)(item - _longIndex)].ToString(_culture);
            else if (item < _doubleIndex)
            {
                var value = _values32[(int)(item - _floatIndex)];
                return Unsafe.As<int, float>(ref value).ToString(_culture);
            }
            else
            {
                var value = _values64[(int)(item - _doubleIndex)];
                return Unsafe.As<long, double>(ref value).ToString(_culture);
            }
        }

        public void Load(Stream stream, bool singlePrecision, bool dictionarizeStrings, params char[] separators)
        {
            using var reader = new StreamReader(stream);
            while (true)
            {
                var line = reader.ReadLine();
                if (line == null)
                    break;
                var start = _columns.Count;
                var count = 0;
                var lineSpan = line.AsSpan();
                foreach (var partRange in new AnyOfSpanSplitEnumerator<char>(lineSpan, separators))
                {
                    var part = lineSpan[partRange];
                    count++;
                    if (part.Length == 0)
                        _columns.Add(_emptyStringIndex);
                    else if (long.TryParse(part, NumberStyles.Integer, _culture, out var longValue))
                    {
                        if (longValue >= sbyte.MinValue && longValue <= sbyte.MaxValue)
                        {
                            _columns.Add((uint)_values8.Count + _sbyteIndex);
                            _values8.Add((sbyte)longValue);
                        }
                        else if (longValue >= int.MinValue && longValue <= int.MaxValue)
                        {
                            _columns.Add((uint)_values32.Count + _intIndex);
                            _values32.Add((int)longValue);
                        }
                        else
                        {
                            _columns.Add((uint)_values64.Count + _longIndex);
                            _values64.Add(longValue);
                        }
                    }
                    else if (singlePrecision && float.TryParse(part, NumberStyles.Float, _culture, out var floatValue))
                    {
                        _columns.Add((uint)_values32.Count + _floatIndex);
                        _values32.Add(Unsafe.As<float, int>(ref floatValue));
                    }
                    else if (!singlePrecision && double.TryParse(part, NumberStyles.Float, _culture, out var doubleValue))
                    {
                        floatValue = (float)doubleValue;
                        if (floatValue == doubleValue)
                        {
                            _columns.Add((uint)_values32.Count + _floatIndex);
                            _values32.Add(Unsafe.As<float, int>(ref floatValue));
                        }
                        else
                        {
                            _columns.Add((uint)_values64.Count + _doubleIndex);
                            _values64.Add(Unsafe.As<double, long>(ref doubleValue));
                        }
                    }
                    else
                    {
                        var str = part.ToString();
                        uint strIndex;
                        if (dictionarizeStrings && _stringDict.TryGetValue(str, out strIndex))
                            _columns.Add(strIndex);
                        else
                        {
                            strIndex = (uint)_strings.Count + _stringIndex;
                            _columns.Add(strIndex);
                            if (dictionarizeStrings)
                                _stringDict.Add(str, strIndex);
                            _strings.Add(str);
                        }
                    }
                }
                _lines.Add((start, count));
            }
        }
    }
}
