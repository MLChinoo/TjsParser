using System;
using System.Collections.Generic;
using TjsParser.Syntax;

namespace TjsParser.Parsing;

public enum RootMode { Auto, Script, Expression }
public enum PreprocessorMode { PreserveAll, ActiveOnly }
public enum ErrorMode { Recover, FailFast }
public enum DiagnosticSeverity { Warning, Error }

public sealed class ParseOptions
{
    public RootMode RootMode { get; set; } = RootMode.Auto;
    public PreprocessorMode PreprocessorMode { get; set; } = PreprocessorMode.PreserveAll;
    public ErrorMode ErrorMode { get; set; } = ErrorMode.Recover;
    public IDictionary<string, int> Defines { get; } = new Dictionary<string, int>(StringComparer.Ordinal);
    public string? EncodingHint { get; set; }
}

public sealed class Diagnostic
{
    internal Diagnostic(string code, DiagnosticSeverity severity, string message, SourceSpan span)
    { Code = code; Severity = severity; Message = message; Span = span; }
    public string Code { get; }
    public DiagnosticSeverity Severity { get; }
    public string Message { get; }
    public SourceSpan Span { get; }
}

public enum CommentKind { Line, Block }

public sealed class CommentTrivia
{
    internal CommentTrivia(CommentKind kind, string text, SourceSpan span) { Kind = kind; Text = text; Span = span; }
    public CommentKind Kind { get; }
    public string Text { get; }
    public SourceSpan Span { get; }
}

public enum PreprocessorDirectiveKind { Set, If, EndIf }

public sealed class PreprocessorDirective
{
    internal PreprocessorDirective(PreprocessorDirectiveKind kind, string expression, SourceSpan span, bool isActive, int depth)
    { Kind = kind; Expression = expression; Span = span; IsActive = isActive; Depth = depth; }
    public PreprocessorDirectiveKind Kind { get; }
    public string Expression { get; }
    public SourceSpan Span { get; }
    public bool IsActive { get; }
    public int Depth { get; }
}

public sealed class PreprocessorRegion
{
    internal PreprocessorRegion(string condition, SourceSpan span, SourceSpan contentSpan, bool isActive, int depth)
    { Condition = condition; Span = span; ContentSpan = contentSpan; IsActive = isActive; Depth = depth; }
    public string Condition { get; }
    public SourceSpan Span { get; }
    public SourceSpan ContentSpan { get; }
    public bool IsActive { get; }
    public int Depth { get; }
}

public sealed class SourceMetadata
{
    internal SourceMetadata(string? path, string encoding, bool hasBom, RootMode rootMode)
    { Path = path; Encoding = encoding; HasBom = hasBom; RootMode = rootMode; }
    public string? Path { get; }
    public string Encoding { get; }
    public bool HasBom { get; }
    public RootMode RootMode { get; internal set; }
}

public sealed class ParseResult
{
    internal ParseResult(DocumentSyntax document, SourceMetadata source, string sourceText, IReadOnlyList<CommentTrivia> comments,
        IReadOnlyList<PreprocessorDirective> directives, IReadOnlyList<PreprocessorRegion> regions,
        IReadOnlyDictionary<string, int> finalDefines, IReadOnlyList<Diagnostic> diagnostics)
    {
        Document = document; Source = source; SourceText = sourceText; Comments = comments;
        Directives = directives; Regions = regions; FinalDefines = finalDefines; Diagnostics = diagnostics;
    }

    public DocumentSyntax Document { get; }
    public SourceMetadata Source { get; }
    public string SourceText { get; }
    public IReadOnlyList<CommentTrivia> Comments { get; }
    public IReadOnlyList<PreprocessorDirective> Directives { get; }
    public IReadOnlyList<PreprocessorRegion> Regions { get; }
    public IReadOnlyDictionary<string, int> FinalDefines { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
    public bool Success
    {
        get
        {
            foreach (var diagnostic in Diagnostics) if (diagnostic.Severity == DiagnosticSeverity.Error) return false;
            return true;
        }
    }
}
