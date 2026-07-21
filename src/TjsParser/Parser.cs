using System;
using System.Collections.Generic;
using TjsParser.IO;
using TjsParser.Parsing;

namespace TjsParser;

public static class Parser
{
    public static ParseResult ParseText(string source, ParseOptions? options = null)
        => ParseText(source, null, "unicode-string", false, options);

    public static ParseResult ParseText(string source, string? sourceName, ParseOptions? options = null)
        => ParseText(source, sourceName, "unicode-string", false, options);

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
