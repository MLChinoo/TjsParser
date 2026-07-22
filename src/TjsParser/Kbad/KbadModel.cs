using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Numerics;

namespace TjsParser.Kbad;

public readonly struct KbadByteSpan
{
    public KbadByteSpan(long offset, long length)
    {
        if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        Offset = offset;
        Length = length;
    }

    public long Offset { get; }
    public long Length { get; }
}

public enum KbadValueKind
{
    Null,
    Void,
    Boolean,
    Integer,
    Real,
    String,
    Octet,
    Array,
    Dictionary
}

public sealed class KbadDocument
{
    internal KbadDocument(string? sourcePath, long fileLength, long trailingByteCount, KbadValue root)
    {
        SourcePath = sourcePath;
        FileLength = fileLength;
        TrailingByteCount = trailingByteCount;
        Root = root;
    }

    public string? SourcePath { get; }
    public string Format => "kbad100";
    public long FileLength { get; }
    public long TrailingByteCount { get; }
    public KbadValue Root { get; }
}

public abstract class KbadValue
{
    protected KbadValue(KbadValueKind kind, KbadByteSpan span)
    {
        Kind = kind;
        Span = span;
    }

    public KbadValueKind Kind { get; }
    public KbadByteSpan Span { get; }
}

public sealed class KbadNullValue : KbadValue
{
    internal KbadNullValue(KbadByteSpan span) : base(KbadValueKind.Null, span) { }
}

public sealed class KbadVoidValue : KbadValue
{
    internal KbadVoidValue(KbadByteSpan span) : base(KbadValueKind.Void, span) { }
}

public sealed class KbadBooleanValue : KbadValue
{
    internal KbadBooleanValue(bool value, KbadByteSpan span) : base(KbadValueKind.Boolean, span) { Value = value; }
    public bool Value { get; }
}

public sealed class KbadIntegerValue : KbadValue
{
    internal KbadIntegerValue(BigInteger value, KbadByteSpan span) : base(KbadValueKind.Integer, span) { Value = value; }
    public BigInteger Value { get; }
}

public sealed class KbadRealValue : KbadValue
{
    internal KbadRealValue(double value, KbadByteSpan span) : base(KbadValueKind.Real, span) { Value = value; }
    public double Value { get; }
}

public sealed class KbadStringValue : KbadValue
{
    internal KbadStringValue(string value, KbadByteSpan span) : base(KbadValueKind.String, span) { Value = value; }
    public string Value { get; }
}

public sealed class KbadOctetValue : KbadValue
{
    private readonly byte[] _data;

    internal KbadOctetValue(byte[] data, KbadByteSpan span) : base(KbadValueKind.Octet, span)
    {
        _data = data;
        Bytes = new ReadOnlyCollection<byte>(_data);
    }

    public IReadOnlyList<byte> Bytes { get; }
    public byte[] ToArray() => (byte[])_data.Clone();
    internal byte[] Data => _data;
}

public sealed class KbadArrayValue : KbadValue
{
    internal KbadArrayValue(IList<KbadValue> elements, KbadByteSpan span) : base(KbadValueKind.Array, span)
    {
        Elements = new ReadOnlyCollection<KbadValue>(elements);
    }

    public IReadOnlyList<KbadValue> Elements { get; }
}

public sealed class KbadDictionaryEntry
{
    internal KbadDictionaryEntry(string key, KbadByteSpan span, KbadByteSpan keySpan, KbadValue value)
    {
        Key = key;
        Span = span;
        KeySpan = keySpan;
        Value = value;
    }

    public string Key { get; }
    public KbadByteSpan Span { get; }
    public KbadByteSpan KeySpan { get; }
    public KbadValue Value { get; }
}

public sealed class KbadDictionaryValue : KbadValue
{
    internal KbadDictionaryValue(IList<KbadDictionaryEntry> entries, KbadByteSpan span) : base(KbadValueKind.Dictionary, span)
    {
        Entries = new ReadOnlyCollection<KbadDictionaryEntry>(entries);
    }

    public IReadOnlyList<KbadDictionaryEntry> Entries { get; }
}

public sealed class KbadReadOptions
{
    public int MaxDepth { get; set; } = 256;
    public int MaxCollectionItems { get; set; } = 1_000_000;
    public int MaxStringCodeUnits { get; set; } = 16_777_216;
    public int MaxOctetBytes { get; set; } = 268_435_456;
    public bool AllowTrailingData { get; set; }
}

public sealed class KbadFormatException : IOException
{
    public KbadFormatException(string message, long byteOffset, string? sourcePath = null)
        : this(message, byteOffset, sourcePath, null) { }

    public KbadFormatException(string message, long byteOffset, string? sourcePath, Exception? innerException)
        : base(FormatMessage(message, byteOffset, sourcePath), innerException)
    {
        ByteOffset = byteOffset;
        SourcePath = sourcePath;
    }

    public long ByteOffset { get; }
    public string? SourcePath { get; }

    private static string FormatMessage(string message, long byteOffset, string? sourcePath)
    {
        var location = "byte offset 0x" + byteOffset.ToString("X");
        return string.IsNullOrEmpty(sourcePath)
            ? message + " (" + location + ")."
            : sourcePath + ": " + message + " (" + location + ").";
    }
}
