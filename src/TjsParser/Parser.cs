using System;
using System.Collections.Generic;
using TjsParser.IO;
using TjsParser.Parsing;

namespace TjsParser;

public enum TjsFileKind
{
    SourceText,
    Tjs2100Bytecode,
    Kbad100BinaryData
}

public sealed class UnsupportedTjsFormatException : NotSupportedException
{
    public UnsupportedTjsFormatException(string sourcePath, TjsFileKind fileKind)
        : base(Describe(fileKind) + " is not supported: " + sourcePath)
    {
        SourcePath = sourcePath;
        FileKind = fileKind;
    }

    public string SourcePath { get; }
    public TjsFileKind FileKind { get; }

    private static string Describe(TjsFileKind fileKind)
    {
        switch (fileKind)
        {
            case TjsFileKind.Tjs2100Bytecode: return "Compiled TJS2 bytecode (TJS2100)";
            case TjsFileKind.Kbad100BinaryData: return "Binary TJS dictionary/array data (KBAD100)";
            default: return "TJS file format '" + fileKind + "'";
        }
    }
}

public static class Parser
{
    public static ParseResult ParseText(string source, ParseOptions? options = null)
        => ParseText(source, null, "unicode-string", false, options);

    public static ParseResult ParseText(string source, string? sourceName, ParseOptions? options = null)
        => ParseText(source, sourceName, "unicode-string", false, options);

    public static TjsFileKind DetectFileKind(string path)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        return SourceLoader.DetectFileKind(path);
    }

    public static ParseResult ParseFile(string path, ParseOptions? options = null)
    {
        if (path == null) throw new ArgumentNullException(nameof(path));
        options ??= new ParseOptions();
        var loaded = SourceLoader.Load(path, options.EncodingHint);
        return ParseText(loaded.Text, path, loaded.Encoding, loaded.HasBom, options);
    }

    private static ParseResult ParseText(string source, string? sourceName, string encoding, bool hasBom, ParseOptions? options)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        options ??= new ParseOptions();
        var sourceText = new SourceText(source);
        var diagnostics = new List<Diagnostic>();
        var preprocessed = Preprocessor.Process(sourceText, options);
        diagnostics.AddRange(preprocessed.Diagnostics);
        var lexer = new Lexer(sourceText, preprocessed.Text, diagnostics);
        var tokens = lexer.Lex();
        var parser = new ParserCore(sourceText, preprocessed.Text, tokens, diagnostics, options);
        var document = parser.ParseDocument();
        var metadata = new SourceMetadata(sourceName, encoding, hasBom, parser.SelectedRootMode);
        var comments = Lexer.CollectComments(sourceText);
        return new ParseResult(document, metadata, source, comments, preprocessed.Directives, preprocessed.Regions,
            preprocessed.Defines, diagnostics);
    }
}
