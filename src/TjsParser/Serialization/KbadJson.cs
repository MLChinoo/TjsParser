using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using TjsParser.Kbad;

namespace TjsParser.Serialization;

public enum KbadJsonShape
{
    Plain,
    Typed
}

public sealed class KbadJsonOptions
{
    public bool Indented { get; set; } = true;
    public bool IncludeByteSpans { get; set; } = true;
    public KbadJsonShape Shape { get; set; } = KbadJsonShape.Plain;
}

public static class KbadJson
{
    public static string Serialize(KbadDocument document, KbadJsonOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(stream, document, options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Stream stream, KbadDocument document, KbadJsonOptions? options = null)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (document == null) throw new ArgumentNullException(nameof(document));
        options ??= new KbadJsonOptions();

        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = options.Indented,
            Encoder = TjsJsonEncoder.Instance
        });
        if (options.Shape == KbadJsonShape.Plain)
        {
            WritePlainValue(writer, document.Root);
            writer.Flush();
            return;
        }

        WriteTypedDocument(writer, document, options);
        writer.Flush();
    }

    private static void WriteTypedDocument(Utf8JsonWriter writer, KbadDocument document, KbadJsonOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("schemaVersion", "1.0");
        writer.WritePropertyName("source");
        writer.WriteStartObject();
        if (document.SourcePath == null) writer.WriteNull("path"); else writer.WriteString("path", document.SourcePath);
        writer.WriteString("format", document.Format);
        writer.WriteNumber("length", document.FileLength);
        writer.WriteNumber("trailingByteCount", document.TrailingByteCount);
        writer.WriteEndObject();
        writer.WritePropertyName("document");
        writer.WriteStartObject();
        writer.WriteString("type", "KbadDocument");
        writer.WritePropertyName("value");
        WriteValue(writer, document.Root, options);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }

    private static void WritePlainValue(Utf8JsonWriter writer, KbadValue value)
    {
        switch (value)
        {
            case KbadNullValue:
                WriteTomlBridgeValue(writer, 1);
                break;
            case KbadVoidValue:
                WriteTomlBridgeValue(writer, 0);
                break;
            case KbadBooleanValue boolean:
                writer.WriteBooleanValue(boolean.Value);
                break;
            case KbadIntegerValue integer:
                writer.WriteRawValue(integer.Value.ToString(CultureInfo.InvariantCulture), false);
                break;
            case KbadRealValue real:
                if (double.IsNaN(real.Value) || double.IsInfinity(real.Value))
                    writer.WriteStringValue(real.Value.ToString("R", CultureInfo.InvariantCulture));
                else
                    writer.WriteNumberValue(real.Value);
                break;
            case KbadStringValue text:
                writer.WriteStringValue(text.Value);
                break;
            case KbadOctetValue octet:
                writer.WriteStringValue(Convert.ToBase64String(octet.Data));
                break;
            case KbadArrayValue array:
                writer.WriteStartArray();
                foreach (var element in array.Elements) WritePlainValue(writer, element);
                writer.WriteEndArray();
                break;
            case KbadDictionaryValue dictionary:
                writer.WriteStartObject();
                foreach (var entry in dictionary.Entries)
                {
                    writer.WritePropertyName(entry.Key);
                    WritePlainValue(writer, entry.Value);
                }
                writer.WriteEndObject();
                break;
            default:
                throw new InvalidOperationException("Unknown KBAD value type: " + value.GetType().FullName);
        }
    }

    private static void WriteTomlBridgeValue(Utf8JsonWriter writer, int typeCode)
    {
        writer.WriteStartObject();
        writer.WriteNumber(string.Empty, typeCode);
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, KbadValue value, KbadJsonOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("type", value.Kind.ToString());
        if (options.IncludeByteSpans)
        {
            writer.WritePropertyName("span");
            WriteSpan(writer, value.Span);
        }

        switch (value)
        {
            case KbadBooleanValue boolean:
                writer.WriteBoolean("value", boolean.Value);
                break;
            case KbadIntegerValue integer:
                writer.WriteString("value", integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case KbadRealValue real:
                writer.WriteString("value", real.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case KbadStringValue text:
                writer.WriteString("value", text.Value);
                break;
            case KbadOctetValue octet:
                writer.WriteString("encoding", "base64");
                writer.WriteString("value", Convert.ToBase64String(octet.Data));
                break;
            case KbadArrayValue array:
                writer.WritePropertyName("elements");
                writer.WriteStartArray();
                foreach (var element in array.Elements) WriteValue(writer, element, options);
                writer.WriteEndArray();
                break;
            case KbadDictionaryValue dictionary:
                writer.WritePropertyName("entries");
                writer.WriteStartArray();
                foreach (var entry in dictionary.Entries)
                {
                    writer.WriteStartObject();
                    if (options.IncludeByteSpans)
                    {
                        writer.WritePropertyName("span"); WriteSpan(writer, entry.Span);
                        writer.WritePropertyName("keySpan"); WriteSpan(writer, entry.KeySpan);
                    }
                    writer.WriteString("key", entry.Key);
                    writer.WritePropertyName("value"); WriteValue(writer, entry.Value, options);
                    writer.WriteEndObject();
                }
                writer.WriteEndArray();
                break;
        }

        writer.WriteEndObject();
    }

    private static void WriteSpan(Utf8JsonWriter writer, KbadByteSpan span)
    {
        writer.WriteStartObject();
        writer.WriteNumber("offset", span.Offset);
        writer.WriteNumber("length", span.Length);
        writer.WriteEndObject();
    }
}
