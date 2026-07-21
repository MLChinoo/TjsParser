using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using TjsParser;
using TjsParser.Parsing;
using TjsParser.Serialization;
using TjsParser.Syntax;
using Xunit;

namespace TjsParser.Tests;

public sealed class ParserTests
{
    [Fact]
    public void ParsesScriptDeclarationsAndTjsOperators()
    {
        const string source = "class Child extends Parent { property value { getter { return _value; } setter(v) { _value = v; } } }\n" +
                              "function call(args*) { return target(args*) incontextof this; }\n" +
                              "var enabled = true; enabled = false if ready;";
        var result = Parser.ParseText(source);
        Assert.True(result.Success, FormatDiagnostics(result));
        var document = Assert.IsType<ScriptDocumentSyntax>(result.Document);
        Assert.IsType<ClassDeclarationSyntax>(document.Body[0]);
        Assert.IsType<FunctionDeclarationSyntax>(document.Body[1]);
        Assert.IsType<VariableDeclarationSyntax>(document.Body[2]);
        Assert.IsType<ExpressionStatementSyntax>(document.Body[3]);
    }

    [Fact]
    public void DetectsDictionaryExpressionDocumentWithoutFlatteningEntries()
    {
        var result = Parser.ParseText("%[ name: \"value\", \"duplicate\" => 1, \"duplicate\", 2 ]");
        Assert.True(result.Success, FormatDiagnostics(result));
        var document = Assert.IsType<ExpressionDocumentSyntax>(result.Document);
        var dictionary = Assert.IsType<DictionaryExpressionSyntax>(document.Expression);
        Assert.Equal(3, dictionary.Entries.Count);
        Assert.Equal(new[] { "Colon", "Arrow", "CommaPair" }, dictionary.Entries.Select(e => e.Separator));
    }

    [Fact]
    public void DetectsConstArrayExpressionDocument()
    {
        var result = Parser.ParseText("(const)[1, (const)%[\"a\", 2]]");
        Assert.True(result.Success, FormatDiagnostics(result));
        var document = Assert.IsType<ExpressionDocumentSyntax>(result.Document);
        var array = Assert.IsType<ArrayExpressionSyntax>(document.Expression);
        Assert.True(array.IsConst);
        Assert.True(Assert.IsType<DictionaryExpressionSyntax>(array.Elements[1].Expression).IsConst);
    }

    [Fact]
    public void ParsesArrowSeparatedCallArguments()
    {
        const string source = "DataExtraInfo.push(\"StoreTime\" => function(ret, key) { return key; } incontextof this);";
        var result = Parser.ParseText(source);

        Assert.True(result.Success, FormatDiagnostics(result));
        var document = Assert.IsType<ScriptDocumentSyntax>(result.Document);
        var statement = Assert.IsType<ExpressionStatementSyntax>(Assert.Single(document.Body));
        var call = Assert.IsType<CallExpressionSyntax>(statement.Expression);
        Assert.Equal(2, call.Arguments.Count);
        Assert.IsType<LiteralExpressionSyntax>(call.Arguments[0].Expression);
        Assert.IsType<BinaryExpressionSyntax>(call.Arguments[1].Expression);
        Assert.False(call.Arguments[0].IsOmitted);
        Assert.False(call.Arguments[1].IsOmitted);
    }

    [Fact]
    public void DetectsAndRejectsTjs2100BytecodeBeforeDecoding()
    {
        var path = Path.Combine(Path.GetTempPath(), "tjs-parser-bytecode-" + Guid.NewGuid().ToString("N") + ".tjs");
        try
        {
            File.WriteAllBytes(path, Encoding.ASCII.GetBytes("TJS2100\0\u0014\u0005\0\0DATA"));
            Assert.Equal(TjsFileKind.Tjs2100Bytecode, Parser.DetectFileKind(path));

            var options = new ParseOptions { EncodingHint = "utf-16le" };
            var exception = Assert.Throws<UnsupportedTjsFormatException>(() => Parser.ParseFile(path, options));
            Assert.Equal(TjsFileKind.Tjs2100Bytecode, exception.FileKind);
            Assert.Equal(path, exception.SourcePath);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void SimilarOrShortHeadersRemainSourceText()
    {
        var nearPath = Path.Combine(Path.GetTempPath(), "tjs-parser-near-header-" + Guid.NewGuid().ToString("N") + ".tjs");
        var shortPath = Path.Combine(Path.GetTempPath(), "tjs-parser-short-header-" + Guid.NewGuid().ToString("N") + ".tjs");
        try
        {
            File.WriteAllBytes(nearPath, Encoding.ASCII.GetBytes("TJS2101\0"));
            File.WriteAllBytes(shortPath, Encoding.ASCII.GetBytes("TJS2"));
            Assert.Equal(TjsFileKind.SourceText, Parser.DetectFileKind(nearPath));
            Assert.Equal(TjsFileKind.SourceText, Parser.DetectFileKind(shortPath));
        }
        finally
        {
            if (File.Exists(nearPath)) File.Delete(nearPath);
            if (File.Exists(shortPath)) File.Delete(shortPath);
        }
    }

    [Fact]
    public void ActivePreprocessingMasksInactiveSourceWithoutChangingOffsets()
    {
        const string source = "@set (FEATURE=0)\n@if (FEATURE)\nvar hidden = @;\n@endif\nvar visible = 1;";
        var options = new ParseOptions { PreprocessorMode = PreprocessorMode.ActiveOnly };
        var result = Parser.ParseText(source, options);
        Assert.True(result.Success, FormatDiagnostics(result));
        var document = Assert.IsType<ScriptDocumentSyntax>(result.Document);
        Assert.Single(document.Body);
        Assert.Equal(5, document.Body[0].Span.Start.Line);
        Assert.False(result.Regions.Single().IsActive);
    }

    [Fact]
    public void JsonUsesStableTaggedAstShape()
    {
        var result = Parser.ParseText("var value = 9223372036854775807;");
        var json = AstJson.Serialize(result, new AstJsonOptions { Indented = false });
        using var document = JsonDocument.Parse(json);
        Assert.Equal("1.0", document.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal("ScriptDocument", document.RootElement.GetProperty("document").GetProperty("type").GetString());
        var literal = document.RootElement.GetProperty("document").GetProperty("body")[0].GetProperty("declarations")[0].GetProperty("initializer");
        Assert.Equal("9223372036854775807", literal.GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("cp932", false)]
    [InlineData("utf-16le", true)]
    public void DetectsGameFileEncodings(string encodingName, bool bom)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var encoding = encodingName == "cp932" ? Encoding.GetEncoding(932) : new UnicodeEncoding(false, bom);
        var path = Path.Combine(Path.GetTempPath(), "tjs-parser-" + Guid.NewGuid().ToString("N") + ".tjs");
        try
        {
            File.WriteAllText(path, "var 日本語 = \"文字列\";", encoding);
            var result = Parser.ParseFile(path);
            Assert.True(result.Success, FormatDiagnostics(result));
            Assert.Equal(encodingName, result.Source.Encoding);
            Assert.Equal(bom, result.Source.HasBom);
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void ParsesConfiguredExternalCorpus()
    {
        var root = Environment.GetEnvironmentVariable("TJS_CORPUS_DIR");
        if (string.IsNullOrWhiteSpace(root)) return;
        var files = Directory.GetFiles(root, "*.tjs", SearchOption.AllDirectories);
        Assert.NotEmpty(files);
        var sourceFiles = files.Where(file => Parser.DetectFileKind(file) == TjsFileKind.SourceText).ToArray();
        Assert.NotEmpty(sourceFiles);
        foreach (var file in sourceFiles)
        {
            var result = Parser.ParseFile(file);
            Assert.True(result.Success, file + Environment.NewLine + FormatDiagnostics(result));

            var activeOptions = new ParseOptions { PreprocessorMode = PreprocessorMode.ActiveOnly };
            activeOptions.Defines["kirikiriz"] = 1;
            activeOptions.Defines["DEBUG"] = 0;
            activeOptions.Defines["PACKED"] = 1;
            var activeResult = Parser.ParseFile(file, activeOptions);
            Assert.True(activeResult.Success, file + " (active preprocessing)" + Environment.NewLine + FormatDiagnostics(activeResult));
        }
    }

    private static string FormatDiagnostics(ParseResult result) => string.Join(Environment.NewLine,
        result.Diagnostics.Select(d => $"{d.Code} ({d.Span.Start.Line},{d.Span.Start.Column}): {d.Message}"));
}
