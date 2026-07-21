using System;
using System.Collections.Generic;
using TjsParser.Syntax;

namespace TjsParser.Parsing;

internal sealed class PreprocessResult
{
    public PreprocessResult(string text, IReadOnlyList<PreprocessorDirective> directives, IReadOnlyList<PreprocessorRegion> regions,
        IReadOnlyDictionary<string, int> defines, IReadOnlyList<Diagnostic> diagnostics)
    { Text = text; Directives = directives; Regions = regions; Defines = defines; Diagnostics = diagnostics; }
    public string Text { get; }
    public IReadOnlyList<PreprocessorDirective> Directives { get; }
    public IReadOnlyList<PreprocessorRegion> Regions { get; }
    public IReadOnlyDictionary<string, int> Defines { get; }
    public IReadOnlyList<Diagnostic> Diagnostics { get; }
}

internal static class Preprocessor
{
    private sealed class Frame
    {
        public string Condition = string.Empty;
        public int Start;
        public int ContentStart;
        public int Depth;
        public bool Active;
        public bool ParentActive;
    }

    public static PreprocessResult Process(SourceText source, ParseOptions options)
    {
        var original = source.Text;
        var output = original.ToCharArray();
        var directives = new List<PreprocessorDirective>();
        var regions = new List<PreprocessorRegion>();
        var diagnostics = new List<Diagnostic>();
        var defines = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in options.Defines) defines[item.Key] = item.Value;
        var stack = new Stack<Frame>();
        var i = 0;

        while (i < original.Length)
        {
            var active = stack.Count == 0 || stack.Peek().Active;
            if (options.PreprocessorMode == PreprocessorMode.ActiveOnly && !active) MaskChar(output, original, i);

            if (original[i] == '/' && i + 1 < original.Length && original[i + 1] == '/')
            {
                var end = original.IndexOf('\n', i + 2);
                if (end < 0) end = original.Length;
                if (options.PreprocessorMode == PreprocessorMode.ActiveOnly && !active) MaskRange(output, original, i, end);
                i = end;
                continue;
            }
            if (original[i] == '/' && i + 1 < original.Length && original[i + 1] == '*')
            {
                var end = ScanBlockComment(original, i);
                if (options.PreprocessorMode == PreprocessorMode.ActiveOnly && !active) MaskRange(output, original, i, end);
                i = end;
                continue;
            }
            if (original[i] == '\'' || original[i] == '"')
            {
                var end = ScanQuoted(original, i, original[i]);
                if (options.PreprocessorMode == PreprocessorMode.ActiveOnly && !active) MaskRange(output, original, i, end);
                i = end;
                continue;
            }
            if (original[i] == '@')
            {
                var quoteAt = i + 1;
                while (quoteAt < original.Length && char.IsWhiteSpace(original[quoteAt])) quoteAt++;
                if (quoteAt < original.Length && (original[quoteAt] == '\'' || original[quoteAt] == '"'))
                {
                    var end = ScanInterpolated(original, quoteAt, original[quoteAt]);
                    if (options.PreprocessorMode == PreprocessorMode.ActiveOnly && !active) MaskRange(output, original, i, end);
                    i = end;
                    continue;
                }

                var kind = MatchDirective(original, i, out var keywordEnd);
                if (kind.HasValue)
                {
                    var depth = stack.Count;
                    var directiveEnd = keywordEnd;
                    var expression = string.Empty;
                    if (kind != PreprocessorDirectiveKind.EndIf)
                    {
                        while (directiveEnd < original.Length && char.IsWhiteSpace(original[directiveEnd])) directiveEnd++;
                        if (directiveEnd >= original.Length || original[directiveEnd] != '(')
                        {
                            diagnostics.Add(new Diagnostic("TJS1001", DiagnosticSeverity.Error, "Preprocessor directive requires a parenthesized expression.", source.Span(i, Math.Min(original.Length, keywordEnd))));
                        }
                        else
                        {
                            var close = ScanBalancedParentheses(original, directiveEnd);
                            if (close < 0)
                            {
                                diagnostics.Add(new Diagnostic("TJS1002", DiagnosticSeverity.Error, "Unclosed preprocessor expression.", source.Span(i, original.Length)));
                                directiveEnd = original.Length;
                            }
                            else
                            {
                                expression = original.Substring(directiveEnd + 1, close - directiveEnd - 1);
                                directiveEnd = close + 1;
                            }
                        }
                    }
                    else
                    {
                        while (directiveEnd < original.Length && original[directiveEnd] != '\r' && original[directiveEnd] != '\n' && char.IsWhiteSpace(original[directiveEnd])) directiveEnd++;
                    }

                    var parentActive = stack.Count == 0 || stack.Peek().Active;
                    var directiveActive = parentActive;
                    if (kind == PreprocessorDirectiveKind.If)
                    {
                        var value = parentActive ? Evaluate(expression, defines, diagnostics, source.Span(i, directiveEnd)) : 0;
                        directiveActive = parentActive && value != 0;
                        stack.Push(new Frame { Condition = expression, Start = i, ContentStart = directiveEnd, Depth = depth, Active = directiveActive, ParentActive = parentActive });
                    }
                    else if (kind == PreprocessorDirectiveKind.Set)
                    {
                        if (parentActive) Evaluate(expression, defines, diagnostics, source.Span(i, directiveEnd));
                    }
                    else
                    {
                        if (stack.Count == 0)
                        {
                            diagnostics.Add(new Diagnostic("TJS1003", DiagnosticSeverity.Error, "@endif has no matching @if.", source.Span(i, directiveEnd)));
                            directiveActive = false;
                        }
                        else
                        {
                            var frame = stack.Pop();
                            directiveActive = frame.ParentActive;
                            regions.Add(new PreprocessorRegion(frame.Condition, source.Span(frame.Start, directiveEnd), source.Span(frame.ContentStart, i), frame.Active, frame.Depth));
                        }
                    }

                    directives.Add(new PreprocessorDirective(kind.Value, expression, source.Span(i, directiveEnd), directiveActive, depth));
                    MaskRange(output, original, i, directiveEnd);
                    i = directiveEnd;
                    continue;
                }
            }
            i++;
        }

        while (stack.Count != 0)
        {
            var frame = stack.Pop();
            diagnostics.Add(new Diagnostic("TJS1004", DiagnosticSeverity.Error, "@if has no matching @endif.", source.Span(frame.Start, original.Length)));
            regions.Add(new PreprocessorRegion(frame.Condition, source.Span(frame.Start, original.Length), source.Span(frame.ContentStart, original.Length), frame.Active, frame.Depth));
        }

        regions.Sort((a, b) => a.Span.Start.Offset.CompareTo(b.Span.Start.Offset));
        return new PreprocessResult(new string(output), directives, regions, defines, diagnostics);
    }

    private static PreprocessorDirectiveKind? MatchDirective(string text, int start, out int end)
    {
        if (MatchWord(text, start + 1, "set", out end)) return PreprocessorDirectiveKind.Set;
        if (MatchWord(text, start + 1, "if", out end)) return PreprocessorDirectiveKind.If;
        if (MatchWord(text, start + 1, "endif", out end)) return PreprocessorDirectiveKind.EndIf;
        end = start + 1;
        return null;
    }

    private static bool MatchWord(string text, int start, string word, out int end)
    {
        end = start + word.Length;
        if (end > text.Length || string.CompareOrdinal(text, start, word, 0, word.Length) != 0) return false;
        return end == text.Length || !IsIdentifierPart(text[end]);
    }

    private static bool IsIdentifierPart(char ch) => ch == '_' || char.IsLetterOrDigit(ch) || ch >= 0x80;

    private static int ScanBlockComment(string text, int start)
    {
        var i = start + 2;
        var depth = 1;
        while (i < text.Length && depth > 0)
        {
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*') { depth++; i += 2; }
            else if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '/') { depth--; i += 2; }
            else i++;
        }
        return i;
    }

    private static int ScanQuoted(string text, int start, char delimiter)
    {
        var i = start + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\') { i = Math.Min(text.Length, i + 2); continue; }
            if (text[i] == delimiter) return i + 1;
            i++;
        }
        return i;
    }

    private static int ScanInterpolated(string text, int quoteAt, char delimiter)
    {
        var i = quoteAt + 1;
        while (i < text.Length)
        {
            if (text[i] == '\\') { i = Math.Min(text.Length, i + 2); continue; }
            if (text[i] == delimiter) return i + 1;
            if (text[i] == '$' && i + 1 < text.Length && text[i + 1] == '{')
            {
                var close = ScanBalanced(text, i + 1, '{', '}');
                i = close < 0 ? text.Length : close + 1;
                continue;
            }
            i++;
        }
        return i;
    }

    private static int ScanBalancedParentheses(string text, int start) => ScanBalanced(text, start, '(', ')');

    private static int ScanBalanced(string text, int start, char open, char close)
    {
        var depth = 0;
        for (var i = start; i < text.Length; i++)
        {
            if (text[i] == '\'' || text[i] == '"') { i = ScanQuoted(text, i, text[i]) - 1; continue; }
            if (text[i] == open) depth++;
            else if (text[i] == close && --depth == 0) return i;
        }
        return -1;
    }

    private static void MaskRange(char[] output, string original, int start, int end)
    {
        for (var i = start; i < end && i < output.Length; i++) MaskChar(output, original, i);
    }

    private static void MaskChar(char[] output, string original, int index)
    {
        if (original[index] != '\r' && original[index] != '\n') output[index] = ' ';
    }

    private static int Evaluate(string expression, IDictionary<string, int> defines, IList<Diagnostic> diagnostics, SourceSpan span)
    {
        try { return new PreprocessorExpressionParser(expression, defines).Parse(); }
        catch (Exception ex)
        {
            diagnostics.Add(new Diagnostic("TJS1005", DiagnosticSeverity.Error, "Invalid preprocessor expression: " + ex.Message, span));
            return 0;
        }
    }
}

internal sealed class PreprocessorExpressionParser
{
    private readonly string _text;
    private readonly IDictionary<string, int> _defines;
    private int _position;

    public PreprocessorExpressionParser(string text, IDictionary<string, int> defines) { _text = text; _defines = defines; }

    public int Parse()
    {
        var value = ParseExpression(0);
        SkipWhite();
        if (_position != _text.Length) throw new FormatException("Unexpected token at offset " + _position + ".");
        return value;
    }

    private int ParseExpression(int minPrecedence)
    {
        SkipWhite();
        var left = ParsePrefix();
        while (true)
        {
            SkipWhite();
            var op = PeekOperator();
            var precedence = Precedence(op);
            if (precedence < minPrecedence) break;
            _position += op.Length;
            if (op == "=")
            {
                throw new FormatException("Assignment target must be an identifier.");
            }
            var right = ParseExpression(precedence + (op == "=" ? 0 : 1));
            left = Apply(op, left, right);
        }
        return left;
    }

    private int ParsePrefix()
    {
        SkipWhite();
        if (Take("!")) return ParsePrefix() == 0 ? 1 : 0;
        if (Take("+")) return ParsePrefix();
        if (Take("-")) return unchecked(-ParsePrefix());
        if (Take("("))
        {
            var value = ParseExpression(0);
            SkipWhite();
            if (!Take(")")) throw new FormatException("Missing ')'.");
            return value;
        }
        if (_position < _text.Length && char.IsDigit(_text[_position])) return ReadNumber();
        var name = ReadIdentifier();
        if (name.Length == 0) throw new FormatException("Expected value at offset " + _position + ".");
        SkipWhite();
        if (PeekOperator() == "=")
        {
            _position++;
            var value = ParseExpression(0);
            _defines[name] = value;
            return value;
        }
        return _defines.TryGetValue(name, out var found) ? found : 0;
    }

    private int ReadNumber()
    {
        var start = _position;
        if (_position + 1 < _text.Length && _text[_position] == '0' && (_text[_position + 1] == 'x' || _text[_position + 1] == 'X'))
        {
            _position += 2;
            while (_position < _text.Length && Uri.IsHexDigit(_text[_position])) _position++;
            return Convert.ToInt32(_text.Substring(start + 2, _position - start - 2), 16);
        }
        while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
        return int.Parse(_text.Substring(start, _position - start), System.Globalization.CultureInfo.InvariantCulture);
    }

    private string ReadIdentifier()
    {
        var start = _position;
        while (_position < _text.Length && (_text[_position] == '_' || char.IsLetterOrDigit(_text[_position]) || _text[_position] >= 0x80)) _position++;
        return _text.Substring(start, _position - start);
    }

    private string PeekOperator()
    {
        foreach (var op in new[] { "||", "&&", "!=", "==", "<=", ">=", ",", "=", "|", "^", "&", "<", ">", "+", "-", "*", "/", "%" })
            if (_position + op.Length <= _text.Length && string.CompareOrdinal(_text, _position, op, 0, op.Length) == 0) return op;
        return string.Empty;
    }

    private static int Precedence(string op)
    {
        switch (op)
        {
            case ",": return 1; case "=": return 2; case "||": return 3; case "&&": return 4; case "|": return 5;
            case "^": return 6; case "&": return 7; case "==": case "!=": return 8; case "<": case ">": case "<=": case ">=": return 9;
            case "+": case "-": return 10; case "*": case "/": case "%": return 11; default: return -1;
        }
    }

    private static int Apply(string op, int left, int right)
    {
        unchecked
        {
            switch (op)
            {
                case ",": return right; case "||": return left != 0 || right != 0 ? 1 : 0; case "&&": return left != 0 && right != 0 ? 1 : 0;
                case "|": return left | right; case "^": return left ^ right; case "&": return left & right;
                case "==": return left == right ? 1 : 0; case "!=": return left != right ? 1 : 0;
                case "<": return left < right ? 1 : 0; case ">": return left > right ? 1 : 0; case "<=": return left <= right ? 1 : 0; case ">=": return left >= right ? 1 : 0;
                case "+": return left + right; case "-": return left - right; case "*": return left * right; case "/": return left / right; case "%": return left % right;
                default: throw new FormatException("Unknown operator " + op + ".");
            }
        }
    }

    private bool Take(string text)
    {
        if (_position + text.Length > _text.Length || string.CompareOrdinal(_text, _position, text, 0, text.Length) != 0) return false;
        _position += text.Length; return true;
    }

    private void SkipWhite() { while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++; }
}
