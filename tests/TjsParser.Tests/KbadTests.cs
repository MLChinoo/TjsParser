using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Text.Json;
using TjsParser.Kbad;
using TjsParser.Serialization;
using Xunit;

namespace TjsParser.Tests;

public sealed class KbadTests
{
    [Fact]
    public void ReadsEveryScalarEncoding()
    {
        using var stream = StartDocument();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((byte)0xDC); writer.Write((ushort)23);
            writer.Write((byte)0x7F);
            writer.Write((byte)0xE0);
            writer.Write((byte)0xC0);
            writer.Write((byte)0xC1);
            writer.Write((byte)0xC2);
            writer.Write((byte)0xC3);
            writer.Write((byte)0xA2); WriteUtf16(writer, "𠮟");
            writer.Write((byte)0xC4); writer.Write((byte)1); WriteUtf16(writer, "A");
            writer.Write((byte)0xC5); writer.Write((ushort)1); WriteUtf16(writer, "B");
            writer.Write((byte)0xC6); writer.Write((uint)1); WriteUtf16(writer, "C");
            writer.Write((byte)0xCA); writer.Write(1.5f);
            writer.Write((byte)0xCB); writer.Write(-2.25d);
            writer.Write((byte)0xCC); writer.Write(byte.MaxValue);
            writer.Write((byte)0xCD); writer.Write(ushort.MaxValue);
            writer.Write((byte)0xCE); writer.Write(uint.MaxValue);
            writer.Write((byte)0xCF); writer.Write(ulong.MaxValue);
            writer.Write((byte)0xD0); writer.Write(sbyte.MinValue);
            writer.Write((byte)0xD1); writer.Write(short.MinValue);
            writer.Write((byte)0xD2); writer.Write(int.MinValue);
            writer.Write((byte)0xD3); writer.Write(long.MinValue);
            writer.Write((byte)0xD6); writer.Write(new byte[] { 1, 2 });
            writer.Write((byte)0xDA); writer.Write((ushort)2); writer.Write(new byte[] { 3, 4 });
            writer.Write((byte)0xDB); writer.Write((uint)2); writer.Write(new byte[] { 5, 6 });
        }

        var document = Read(stream);
        var array = Assert.IsType<KbadArrayValue>(document.Root);
        Assert.Equal(23, array.Elements.Count);
        Assert.Equal(new BigInteger(127), Integer(array, 0));
        Assert.Equal(new BigInteger(-32), Integer(array, 1));
        Assert.IsType<KbadNullValue>(array.Elements[2]);
        Assert.IsType<KbadVoidValue>(array.Elements[3]);
        Assert.True(Assert.IsType<KbadBooleanValue>(array.Elements[4]).Value);
        Assert.False(Assert.IsType<KbadBooleanValue>(array.Elements[5]).Value);
        Assert.Equal(new[] { "𠮟", "A", "B", "C" }, array.Elements.Skip(6).Take(4).Cast<KbadStringValue>().Select(v => v.Value));
        Assert.Equal(1.5d, Assert.IsType<KbadRealValue>(array.Elements[10]).Value);
        Assert.Equal(-2.25d, Assert.IsType<KbadRealValue>(array.Elements[11]).Value);
        Assert.Equal(new BigInteger(byte.MaxValue), Integer(array, 12));
        Assert.Equal(new BigInteger(ushort.MaxValue), Integer(array, 13));
        Assert.Equal(new BigInteger(uint.MaxValue), Integer(array, 14));
        Assert.Equal(new BigInteger(ulong.MaxValue), Integer(array, 15));
        Assert.Equal(new BigInteger(sbyte.MinValue), Integer(array, 16));
        Assert.Equal(new BigInteger(short.MinValue), Integer(array, 17));
        Assert.Equal(new BigInteger(int.MinValue), Integer(array, 18));
        Assert.Equal(new BigInteger(long.MinValue), Integer(array, 19));
        Assert.Equal(new byte[] { 1, 2 }, Assert.IsType<KbadOctetValue>(array.Elements[20]).ToArray());
        Assert.Equal(new byte[] { 3, 4 }, Assert.IsType<KbadOctetValue>(array.Elements[21]).ToArray());
        Assert.Equal(new byte[] { 5, 6 }, Assert.IsType<KbadOctetValue>(array.Elements[22]).ToArray());
        Assert.Equal(stream.Length, document.FileLength);
        Assert.Equal(0, document.TrailingByteCount);
        Assert.Equal(8, array.Span.Offset);
        Assert.Equal(stream.Length - 8, array.Span.Length);
    }

    [Fact]
    public void ReadsAllCollectionCountEncodingsAndDictionaryKeyEncodings()
    {
        using var stream = StartDocument();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((byte)0xDD); writer.Write((uint)4);
            writer.Write((byte)0x81); writer.Write((byte)0xA1); WriteUtf16(writer, "a");
            writer.Write((byte)0x91); writer.Write((byte)1);
            writer.Write((byte)0xDE); writer.Write((ushort)1); writer.Write((byte)0xC4); writer.Write((byte)1); WriteUtf16(writer, "b");
            writer.Write((byte)0xDC); writer.Write((ushort)0);
            writer.Write((byte)0xDF); writer.Write((uint)1); writer.Write((byte)0xC6); writer.Write((uint)1); WriteUtf16(writer, "c");
            writer.Write((byte)0xDD); writer.Write((uint)0);
            writer.Write((byte)0x81); writer.Write((byte)0xC5); writer.Write((ushort)1); WriteUtf16(writer, "d");
            writer.Write((byte)0x90);
        }

        var root = Assert.IsType<KbadArrayValue>(Read(stream).Root);
        Assert.Equal(4, root.Elements.Count);
        var first = Assert.IsType<KbadDictionaryValue>(root.Elements[0]);
        Assert.Equal("a", Assert.Single(first.Entries).Key);
        Assert.Single(Assert.IsType<KbadArrayValue>(first.Entries[0].Value).Elements);
        var second = Assert.IsType<KbadDictionaryValue>(root.Elements[1]);
        Assert.Equal("b", Assert.Single(second.Entries).Key);
        Assert.Empty(Assert.IsType<KbadArrayValue>(second.Entries[0].Value).Elements);
        var third = Assert.IsType<KbadDictionaryValue>(root.Elements[2]);
        Assert.Equal("c", Assert.Single(third.Entries).Key);
        Assert.Empty(Assert.IsType<KbadArrayValue>(third.Entries[0].Value).Elements);
        var fourth = Assert.IsType<KbadDictionaryValue>(root.Elements[3]);
        Assert.Equal("d", Assert.Single(fourth.Entries).Key);
        Assert.Empty(Assert.IsType<KbadArrayValue>(fourth.Entries[0].Value).Elements);
        Assert.True(first.Entries[0].KeySpan.Length > 1);
        Assert.True(first.Entries[0].Span.Length > first.Entries[0].KeySpan.Length);
    }

    [Fact]
    public void JsonPreservesTaggedValuesUnicodeAndOctets()
    {
        using var stream = StartDocument();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((byte)0x83);
            writer.Write((byte)0xA2); WriteUtf16(writer, "文字"); writer.Write((byte)0xA2); WriteUtf16(writer, "𠮟");
            writer.Write((byte)0xA3); WriteUtf16(writer, "nil"); writer.Write((byte)0xC0);
            writer.Write((byte)0xA3); WriteUtf16(writer, "raw"); writer.Write((byte)0xD6); writer.Write(new byte[] { 1, 2 });
        }

        var json = KbadJson.Serialize(Read(stream), new KbadJsonOptions { Indented = false, Shape = KbadJsonShape.Typed });
        Assert.Contains("文字", json);
        Assert.Contains("𠮟", json);
        Assert.Contains("AQI=", json);
        Assert.DoesNotContain("\\u", json, StringComparison.OrdinalIgnoreCase);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("1.0", parsed.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("KbadDocument", parsed.RootElement.GetProperty("document").GetProperty("type").GetString());
        var root = parsed.RootElement.GetProperty("document").GetProperty("value");
        Assert.Equal("Dictionary", root.GetProperty("type").GetString());
        Assert.Equal("Null", root.GetProperty("entries")[1].GetProperty("value").GetProperty("type").GetString());
    }

    [Fact]
    public void PlainJsonUsesTomlBridgeValuesAndNativeKeyValueData()
    {
        using var stream = StartDocument();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, true))
        {
            writer.Write((byte)0x83);
            writer.Write((byte)0xA5); WriteUtf16(writer, "title"); writer.Write((byte)0xA2); WriteUtf16(writer, "文字");
            writer.Write((byte)0xA7); WriteUtf16(writer, "integer"); writer.Write((byte)0xCF); writer.Write(ulong.MaxValue);
            writer.Write((byte)0xA4); WriteUtf16(writer, "list"); writer.Write((byte)0x93);
            writer.Write((byte)0xC0); writer.Write((byte)0xC1); writer.Write((byte)0xD6); writer.Write(new byte[] { 1, 2 });
        }

        var json = KbadJson.Serialize(Read(stream), new KbadJsonOptions { Indented = false });
        Assert.DoesNotContain("schemaVersion", json);
        Assert.DoesNotContain("\"type\"", json);
        Assert.DoesNotContain("\\u", json, StringComparison.OrdinalIgnoreCase);
        using var parsed = JsonDocument.Parse(json);
        Assert.Equal("文字", parsed.RootElement.GetProperty("title").GetString());
        Assert.Equal(ulong.MaxValue.ToString(), parsed.RootElement.GetProperty("integer").GetRawText());
        var list = parsed.RootElement.GetProperty("list");
        Assert.Equal(1, list[0].GetProperty(string.Empty).GetInt32());
        Assert.Equal(0, list[1].GetProperty(string.Empty).GetInt32());
        Assert.Equal("AQI=", list[2].GetString());
    }

    [Fact]
    public void RejectsMalformedDataWithByteOffsets()
    {
        var unknown = Assert.Throws<KbadFormatException>(() => Read(Payload(0xC7)));
        Assert.Equal(8, unknown.ByteOffset);
        Assert.Contains("0xC7", unknown.Message);

        var badKey = Assert.Throws<KbadFormatException>(() => Read(Payload(0x81, 0x01, 0x00)));
        Assert.Equal(9, badKey.ByteOffset);
        Assert.Contains("key must be a string", badKey.Message);

        var truncated = Assert.Throws<KbadFormatException>(() => Read(Payload(0xCB, 0x00, 0x00)));
        Assert.True(truncated.ByteOffset >= 9);
        Assert.Contains("Unexpected end", truncated.Message);
    }

    [Fact]
    public void EnforcesLimitsAndStrictTrailingData()
    {
        var nested = Payload(0x91, 0x91, 0x00);
        var depthError = Assert.Throws<KbadFormatException>(() => Read(nested, new KbadReadOptions { MaxDepth = 1 }));
        Assert.Contains("nesting depth", depthError.Message);

        var octet = Payload(0xDA, 0x02, 0x00, 0x01, 0x02);
        var lengthError = Assert.Throws<KbadFormatException>(() => Read(octet, new KbadReadOptions { MaxOctetBytes = 1 }));
        Assert.Contains("octet length", lengthError.Message);

        var trailing = Payload(0x00, 0xFF);
        Assert.Throws<KbadFormatException>(() => Read(trailing));
        var allowed = Read(trailing, new KbadReadOptions { AllowTrailingData = true });
        Assert.Equal(1, allowed.TrailingByteCount);
    }

    [Fact]
    public void RejectsInvalidSignature()
    {
        using var stream = new MemoryStream(Encoding.ASCII.GetBytes("KBAD101\0\0"));
        var exception = Assert.Throws<KbadFormatException>(() => KbadReader.Read(stream, "invalid.tjs"));
        Assert.Equal(0, exception.ByteOffset);
        Assert.Equal("invalid.tjs", exception.SourcePath);
    }

    private static BigInteger Integer(KbadArrayValue array, int index)
        => Assert.IsType<KbadIntegerValue>(array.Elements[index]).Value;

    private static MemoryStream StartDocument()
    {
        var stream = new MemoryStream();
        var header = Encoding.ASCII.GetBytes("KBAD100\0");
        stream.Write(header, 0, header.Length);
        return stream;
    }

    private static MemoryStream Payload(params byte[] payload)
    {
        var stream = StartDocument();
        stream.Write(payload, 0, payload.Length);
        stream.Position = 0;
        return stream;
    }

    private static KbadDocument Read(MemoryStream stream, KbadReadOptions? options = null)
    {
        stream.Position = 0;
        return KbadReader.Read(stream, "memory.tjs", options);
    }

    private static void WriteUtf16(BinaryWriter writer, string value)
        => writer.Write(Encoding.Unicode.GetBytes(value));
}
