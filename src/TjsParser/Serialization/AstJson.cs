using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.Json;
using TjsParser.Parsing;
using TjsParser.Syntax;

namespace TjsParser.Serialization;

public sealed class AstJsonOptions
{
    public bool Indented { get; set; } = true;
    public bool IncludeComments { get; set; } = true;
    public bool IncludeDiagnostics { get; set; } = true;
}

public static class AstJson
{
    public static string Serialize(ParseResult result, AstJsonOptions? options = null)
    {
        using var stream = new MemoryStream();
        Write(stream, result, options);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Write(Stream stream, ParseResult result, AstJsonOptions? options = null)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (result == null) throw new ArgumentNullException(nameof(result));
        options ??= new AstJsonOptions();
        using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = options.Indented,
            Encoder = TjsJsonEncoder.Instance
        });
        writer.WriteStartObject();
        WriteSource(writer, result.Source);
        writer.WritePropertyName("document"); WriteNode(writer, result.Document);
        if (options.IncludeComments) WriteComments(writer, result.Comments);
        WritePreprocessor(writer, result);
        if (options.IncludeDiagnostics) WriteDiagnostics(writer, result.Diagnostics);
        writer.WriteEndObject();
        writer.Flush();
    }

    private static void WriteSource(Utf8JsonWriter writer, SourceMetadata source)
    {
        writer.WritePropertyName("source"); writer.WriteStartObject();
        if (source.Path == null) writer.WriteNull("path"); else writer.WriteString("path", source.Path);
        writer.WriteString("encoding", source.Encoding);
        writer.WriteBoolean("hasBom", source.HasBom);
        writer.WriteString("rootMode", EnumName(source.RootMode));
        writer.WriteEndObject();
    }

    private static void WriteComments(Utf8JsonWriter writer, IReadOnlyList<CommentTrivia> comments)
    {
        writer.WritePropertyName("comments"); writer.WriteStartArray();
        foreach (var comment in comments)
        {
            writer.WriteStartObject(); writer.WriteString("kind", EnumName(comment.Kind)); writer.WriteString("text", comment.Text);
            writer.WritePropertyName("span"); WriteSpan(writer, comment.Span); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WritePreprocessor(Utf8JsonWriter writer, ParseResult result)
    {
        writer.WritePropertyName("preprocessor"); writer.WriteStartObject();
        writer.WritePropertyName("directives"); writer.WriteStartArray();
        foreach (var directive in result.Directives)
        {
            writer.WriteStartObject(); writer.WriteString("kind", EnumName(directive.Kind)); writer.WriteString("expression", directive.Expression);
            writer.WriteBoolean("isActive", directive.IsActive); writer.WriteNumber("depth", directive.Depth);
            writer.WritePropertyName("span"); WriteSpan(writer, directive.Span); writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("regions"); writer.WriteStartArray();
        foreach (var region in result.Regions)
        {
            writer.WriteStartObject(); writer.WriteString("condition", region.Condition); writer.WriteBoolean("isActive", region.IsActive); writer.WriteNumber("depth", region.Depth);
            writer.WritePropertyName("span"); WriteSpan(writer, region.Span); writer.WritePropertyName("contentSpan"); WriteSpan(writer, region.ContentSpan); writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WritePropertyName("finalDefines"); writer.WriteStartObject();
        var keys = new List<string>(result.FinalDefines.Keys); keys.Sort(StringComparer.Ordinal);
        foreach (var key in keys) writer.WriteNumber(key, result.FinalDefines[key]);
        writer.WriteEndObject(); writer.WriteEndObject();
    }

    private static void WriteDiagnostics(Utf8JsonWriter writer, IReadOnlyList<Diagnostic> diagnostics)
    {
        writer.WritePropertyName("diagnostics"); writer.WriteStartArray();
        foreach (var diagnostic in diagnostics)
        {
            writer.WriteStartObject(); writer.WriteString("code", diagnostic.Code); writer.WriteString("severity", EnumName(diagnostic.Severity));
            writer.WriteString("message", diagnostic.Message); writer.WritePropertyName("span"); WriteSpan(writer, diagnostic.Span); writer.WriteEndObject();
        }
        writer.WriteEndArray();
    }

    private static void WriteNode(Utf8JsonWriter writer, SyntaxNode node)
    {
        writer.WriteStartObject(); writer.WriteString("type", node.Kind.ToString()); writer.WritePropertyName("span"); WriteSpan(writer, node.Span);
        if (node is LiteralExpressionSyntax literal)
        {
            writer.WriteString("literalKind", EnumName(literal.LiteralKind)); writer.WritePropertyName("value"); WriteLiteralValue(writer, literal.Value); writer.WriteString("raw", literal.Raw);
        }
        else
        {
            var properties = node.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public);
            Array.Sort(properties, (a, b) => a.MetadataToken.CompareTo(b.MetadataToken));
            foreach (var property in properties)
            {
                if (property.Name == nameof(SyntaxNode.Kind) || property.Name == nameof(SyntaxNode.Span)) continue;
                writer.WritePropertyName(Camel(property.Name)); WriteValue(writer, property.GetValue(node, null));
            }
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        if (value == null) { writer.WriteNullValue(); return; }
        if (value is SyntaxNode node) { WriteNode(writer, node); return; }
        if (value is SourceSpan span) { WriteSpan(writer, span); return; }
        if (value is string text) { writer.WriteStringValue(text); return; }
        if (value is bool boolean) { writer.WriteBooleanValue(boolean); return; }
        if (value is Enum enumeration) { writer.WriteStringValue(EnumName(enumeration)); return; }
        if (value is int integer) { writer.WriteNumberValue(integer); return; }
        if (value is long longValue) { writer.WriteStringValue(longValue.ToString(CultureInfo.InvariantCulture)); return; }
        if (value is double doubleValue) { writer.WriteStringValue(doubleValue.ToString("R", CultureInfo.InvariantCulture)); return; }
        if (value is IEnumerable enumerable)
        {
            writer.WriteStartArray(); foreach (var item in enumerable) WriteValue(writer, item); writer.WriteEndArray(); return;
        }
        writer.WriteStringValue(Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static void WriteLiteralValue(Utf8JsonWriter writer, object? value)
    {
        if (value is long longValue) writer.WriteStringValue(longValue.ToString(CultureInfo.InvariantCulture));
        else if (value is double doubleValue) writer.WriteStringValue(doubleValue.ToString("R", CultureInfo.InvariantCulture));
        else WriteValue(writer, value);
    }

    private static void WriteSpan(Utf8JsonWriter writer, SourceSpan span)
    {
        writer.WriteStartObject(); writer.WritePropertyName("start"); WritePosition(writer, span.Start); writer.WritePropertyName("end"); WritePosition(writer, span.End); writer.WriteEndObject();
    }

    private static void WritePosition(Utf8JsonWriter writer, SourcePosition position)
    {
        writer.WriteStartObject(); writer.WriteNumber("offset", position.Offset); writer.WriteNumber("line", position.Line); writer.WriteNumber("column", position.Column); writer.WriteEndObject();
    }

    private static string Camel(string value) => value.Length == 0 ? value : char.ToLowerInvariant(value[0]) + value.Substring(1);
    private static string EnumName(object value) { var text = value.ToString() ?? string.Empty; return Camel(text); }
}
