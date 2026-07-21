using System.Collections.Generic;
using TjsParser.Syntax;

namespace TjsParser.Parsing;

internal enum TokenKind { End, Identifier, Number, String, InterpolatedString, RegExp, Octet, Symbol, Invalid }

internal sealed class Token
{
    public Token(TokenKind kind, string text, int start, int end, object? value = null)
    { Kind = kind; Text = text; Start = start; End = end; Value = value; }
    public TokenKind Kind { get; }
    public string Text { get; }
    public int Start { get; }
    public int End { get; }
    public object? Value { get; }
}

internal sealed class InterpolatedTokenPart
{
    public InterpolatedTokenPart(bool isExpression, int start, int end, string delimiter, string text)
    { IsExpression = isExpression; Start = start; End = end; Delimiter = delimiter; Text = text; }
    public bool IsExpression { get; }
    public int Start { get; }
    public int End { get; }
    public string Delimiter { get; }
    public string Text { get; }
}

internal sealed class InterpolatedTokenData
{
    public InterpolatedTokenData(IReadOnlyList<InterpolatedTokenPart> parts) => Parts = parts;
    public IReadOnlyList<InterpolatedTokenPart> Parts { get; }
}

internal sealed class RegExpTokenData
{
    public RegExpTokenData(string pattern, string flags) { Pattern = pattern; Flags = flags; }
    public string Pattern { get; }
    public string Flags { get; }
}
