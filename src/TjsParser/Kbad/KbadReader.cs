using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;

namespace TjsParser.Kbad;

public static class KbadReader
{
    public static KbadDocument ReadFile(string path, KbadReadOptions? options = null)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        using var stream = File.OpenRead(path);
        return Read(stream, path, options);
    }

    public static KbadDocument Read(Stream stream, string? sourceName = null, KbadReadOptions? options = null)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));
        return new Reader(stream, sourceName, options ?? new KbadReadOptions()).ReadDocument();
    }

    private sealed class Reader
    {
        private static readonly byte[] Signature = Encoding.ASCII.GetBytes("KBAD100\0");
        private static readonly Encoding Utf16 = new UnicodeEncoding(false, false, true);

        private readonly Stream _stream;
        private readonly string? _sourceName;
        private readonly KbadReadOptions _options;
        private long _offset;

        public Reader(Stream stream, string? sourceName, KbadReadOptions options)
        {
            ValidateOptions(options);
            _stream = stream;
            _sourceName = sourceName;
            _options = options;
        }

        public KbadDocument ReadDocument()
        {
            var header = ReadExact(Signature.Length);
            for (var i = 0; i < Signature.Length; i++)
                if (header[i] != Signature[i]) throw Error("Invalid KBAD100 signature", 0);

            var root = ReadValue(0);
            var rootEnd = _offset;
            var trailing = 0L;
            int next;
            while ((next = _stream.ReadByte()) >= 0)
            {
                _offset++;
                trailing++;
                if (!_options.AllowTrailingData)
                    throw Error("Unexpected trailing data", rootEnd);
            }

            return new KbadDocument(_sourceName, _offset, trailing, root);
        }

        private KbadValue ReadValue(int depth)
        {
            if (depth > _options.MaxDepth) throw Error("Maximum KBAD nesting depth exceeded", _offset);
            var start = _offset;
            var tag = ReadRequiredByte();

            if (tag <= 0x7F) return Integer(tag, start);
            if (tag >= 0xE0) return Integer(unchecked((sbyte)tag), start);
            if (tag >= 0x80 && tag <= 0x8F) return ReadDictionary((uint)(tag - 0x80), start, depth);
            if (tag >= 0x90 && tag <= 0x9F) return ReadArray((uint)(tag - 0x90), start, depth);
            if (tag >= 0xA0 && tag <= 0xBF) return String(ReadStringPayload((uint)(tag - 0xA0), start), start);

            switch (tag)
            {
                case 0xC0: return new KbadNullValue(Span(start));
                case 0xC1: return new KbadVoidValue(Span(start));
                case 0xC2: return new KbadBooleanValue(true, Span(start));
                case 0xC3: return new KbadBooleanValue(false, Span(start));
                case 0xC4: return String(ReadStringPayload(ReadRequiredByte(), start), start);
                case 0xC5: return String(ReadStringPayload(ReadUInt16(), start), start);
                case 0xC6: return String(ReadStringPayload(ReadUInt32(), start), start);
                case 0xCA: return new KbadRealValue(ReadSingle(), Span(start));
                case 0xCB: return new KbadRealValue(ReadDouble(), Span(start));
                case 0xCC: return Integer(ReadRequiredByte(), start);
                case 0xCD: return Integer(ReadUInt16(), start);
                case 0xCE: return Integer(ReadUInt32(), start);
                case 0xCF: return Integer(new BigInteger(ReadUInt64()), start);
                case 0xD0: return Integer(unchecked((sbyte)ReadRequiredByte()), start);
                case 0xD1: return Integer(unchecked((short)ReadUInt16()), start);
                case 0xD2: return Integer(unchecked((int)ReadUInt32()), start);
                case 0xD3: return Integer(new BigInteger(unchecked((long)ReadUInt64())), start);
                case 0xDA: return Octet(ReadOctets(ReadUInt16(), start), start);
                case 0xDB: return Octet(ReadOctets(ReadUInt32(), start), start);
                case 0xDC: return ReadArray(ReadUInt16(), start, depth);
                case 0xDD: return ReadArray(ReadUInt32(), start, depth);
                case 0xDE: return ReadDictionary(ReadUInt16(), start, depth);
                case 0xDF: return ReadDictionary(ReadUInt32(), start, depth);
                default:
                    if (tag >= 0xD4 && tag <= 0xD9)
                        return Octet(ReadOctets((uint)(tag - 0xD4), start), start);
                    throw Error("Unknown KBAD type tag 0x" + tag.ToString("X2"), start);
            }
        }

        private KbadArrayValue ReadArray(uint rawCount, long start, int depth)
        {
            var count = CollectionCount(rawCount, "array", start);
            var elements = new List<KbadValue>(count);
            for (var i = 0; i < count; i++) elements.Add(ReadValue(depth + 1));
            return new KbadArrayValue(elements, Span(start));
        }

        private KbadDictionaryValue ReadDictionary(uint rawCount, long start, int depth)
        {
            var count = CollectionCount(rawCount, "dictionary", start);
            var entries = new List<KbadDictionaryEntry>(count);
            for (var i = 0; i < count; i++)
            {
                var entryStart = _offset;
                var key = ReadDictionaryKey();
                var value = ReadValue(depth + 1);
                entries.Add(new KbadDictionaryEntry(key.Value, new KbadByteSpan(entryStart, _offset - entryStart), key.Span, value));
            }
            return new KbadDictionaryValue(entries, Span(start));
        }

        private StringResult ReadDictionaryKey()
        {
            var start = _offset;
            var tag = ReadRequiredByte();
            uint length;
            if (tag >= 0xA0 && tag <= 0xBF) length = (uint)(tag - 0xA0);
            else if (tag == 0xC4) length = ReadRequiredByte();
            else if (tag == 0xC5) length = ReadUInt16();
            else if (tag == 0xC6) length = ReadUInt32();
            else throw Error("Dictionary key must be a string, found tag 0x" + tag.ToString("X2"), start);

            var value = ReadStringPayload(length, start);
            return new StringResult(value, Span(start));
        }

        private string ReadStringPayload(uint rawCodeUnits, long start)
        {
            if (rawCodeUnits > int.MaxValue || rawCodeUnits > (uint)_options.MaxStringCodeUnits)
                throw Error("KBAD string length exceeds the configured limit", start);
            int byteCount;
            try { byteCount = checked((int)rawCodeUnits * 2); }
            catch (OverflowException ex) { throw Error("KBAD string byte length overflow", start, ex); }
            var bytes = ReadExact(byteCount);
            try { return Utf16.GetString(bytes); }
            catch (DecoderFallbackException ex) { throw Error("Invalid UTF-16LE string", start, ex); }
        }

        private byte[] ReadOctets(uint rawLength, long start)
        {
            if (rawLength > int.MaxValue || rawLength > (uint)_options.MaxOctetBytes)
                throw Error("KBAD octet length exceeds the configured limit", start);
            return ReadExact((int)rawLength);
        }

        private int CollectionCount(uint rawCount, string kind, long start)
        {
            if (rawCount > int.MaxValue || rawCount > (uint)_options.MaxCollectionItems)
                throw Error("KBAD " + kind + " count exceeds the configured limit", start);
            return (int)rawCount;
        }

        private KbadIntegerValue Integer(long value, long start) => Integer(new BigInteger(value), start);
        private KbadIntegerValue Integer(ulong value, long start) => Integer(new BigInteger(value), start);
        private KbadIntegerValue Integer(BigInteger value, long start) => new KbadIntegerValue(value, Span(start));
        private KbadStringValue String(string value, long start) => new KbadStringValue(value, Span(start));
        private KbadOctetValue Octet(byte[] value, long start) => new KbadOctetValue(value, Span(start));
        private KbadByteSpan Span(long start) => new KbadByteSpan(start, _offset - start);

        private byte ReadRequiredByte()
        {
            var value = _stream.ReadByte();
            if (value < 0) throw Error("Unexpected end of KBAD data", _offset);
            _offset++;
            return (byte)value;
        }

        private byte[] ReadExact(int count)
        {
            var start = _offset;
            var buffer = new byte[count];
            var readTotal = 0;
            while (readTotal < count)
            {
                var read = _stream.Read(buffer, readTotal, count - readTotal);
                if (read <= 0) throw Error("Unexpected end of KBAD data", start + readTotal);
                readTotal += read;
                _offset += read;
            }
            return buffer;
        }

        private ushort ReadUInt16()
        {
            var b0 = ReadRequiredByte(); var b1 = ReadRequiredByte();
            return (ushort)(b0 | (b1 << 8));
        }

        private uint ReadUInt32()
        {
            var b0 = ReadRequiredByte(); var b1 = ReadRequiredByte(); var b2 = ReadRequiredByte(); var b3 = ReadRequiredByte();
            return (uint)(b0 | (b1 << 8) | (b2 << 16) | (b3 << 24));
        }

        private ulong ReadUInt64()
        {
            ulong result = 0;
            for (var shift = 0; shift < 64; shift += 8) result |= (ulong)ReadRequiredByte() << shift;
            return result;
        }

        private float ReadSingle()
        {
            var bytes = ReadExact(4);
            if (!BitConverter.IsLittleEndian) Array.Reverse(bytes);
            return BitConverter.ToSingle(bytes, 0);
        }

        private double ReadDouble() => BitConverter.Int64BitsToDouble(unchecked((long)ReadUInt64()));

        private KbadFormatException Error(string message, long offset, Exception? inner = null)
            => new KbadFormatException(message, offset, _sourceName, inner);

        private static void ValidateOptions(KbadReadOptions options)
        {
            if (options.MaxDepth < 0) throw new ArgumentOutOfRangeException(nameof(options.MaxDepth));
            if (options.MaxCollectionItems <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxCollectionItems));
            if (options.MaxStringCodeUnits <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxStringCodeUnits));
            if (options.MaxOctetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(options.MaxOctetBytes));
        }

        private readonly struct StringResult
        {
            public StringResult(string value, KbadByteSpan span) { Value = value; Span = span; }
            public string Value { get; }
            public KbadByteSpan Span { get; }
        }
    }
}
