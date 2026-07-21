using System;
using System.IO;
using System.Text;
using TjsParser.Parsing;

namespace TjsParser.IO;

internal sealed class LoadedSource
{
    public LoadedSource(string text, string encoding, bool hasBom) { Text = text; Encoding = encoding; HasBom = hasBom; }
    public string Text { get; }
    public string Encoding { get; }
    public bool HasBom { get; }
}

internal static class SourceLoader
{
    static SourceLoader()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static LoadedSource Load(string path, string? hint)
    {
        var bytes = File.ReadAllBytes(path);
        if (!string.IsNullOrWhiteSpace(hint)) return DecodeHint(bytes, hint!);
        if (Starts(bytes, 0xEF, 0xBB, 0xBF)) return Decode(bytes, new UTF8Encoding(false, true), 3, "utf-8", true);
        if (Starts(bytes, 0xFF, 0xFE)) return Decode(bytes, new UnicodeEncoding(false, true, true), 2, "utf-16le", true);
        if (Starts(bytes, 0xFE, 0xFF)) return Decode(bytes, new UnicodeEncoding(true, true, true), 2, "utf-16be", true);

        try { return Decode(bytes, new UTF8Encoding(false, true), 0, "utf-8", false); }
        catch (DecoderFallbackException)
        {
            var cp932 = Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback);
            return Decode(bytes, cp932, 0, "cp932", false);
        }
    }

    private static LoadedSource DecodeHint(byte[] bytes, string hint)
    {
        switch (hint.Trim().ToLowerInvariant().Replace("_", "-"))
        {
            case "utf8": case "utf-8": return Decode(bytes, new UTF8Encoding(false, true), Starts(bytes, 0xEF, 0xBB, 0xBF) ? 3 : 0, "utf-8", Starts(bytes, 0xEF, 0xBB, 0xBF));
            case "utf16": case "utf-16": case "utf-16le": return Decode(bytes, new UnicodeEncoding(false, true, true), Starts(bytes, 0xFF, 0xFE) ? 2 : 0, "utf-16le", Starts(bytes, 0xFF, 0xFE));
            case "utf-16be": return Decode(bytes, new UnicodeEncoding(true, true, true), Starts(bytes, 0xFE, 0xFF) ? 2 : 0, "utf-16be", Starts(bytes, 0xFE, 0xFF));
            case "cp932": case "shift-jis": case "shift_jis": case "sjis":
                return Decode(bytes, Encoding.GetEncoding(932, EncoderFallback.ExceptionFallback, DecoderFallback.ExceptionFallback), 0, "cp932", false);
            default: throw new ArgumentException("Unsupported encoding hint: " + hint, nameof(hint));
        }
    }

    private static LoadedSource Decode(byte[] bytes, Encoding encoding, int skip, string name, bool bom)
        => new LoadedSource(encoding.GetString(bytes, skip, bytes.Length - skip), name, bom);

    private static bool Starts(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++) if (bytes[i] != prefix[i]) return false;
        return true;
    }
}
