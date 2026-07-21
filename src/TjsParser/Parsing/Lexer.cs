using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace TjsParser.Parsing;

internal sealed class Lexer
{
    private static readonly string[] Symbols =
    {
        ">>>=", "===", "!==", "<->", "...", ">>>", ">>=", "<<=", "&&=", "||=",
        ">>", "<<", ">=", "<=", "==", "!=", "=>", "++", "--", "+=", "-=", "*=", "/=", "\\=", "%=", "^=", "&=", "|=", "&&", "||",
        ">", "<", "=", "!", "&", "|", "^", "+", "-", "*", "/", "\\", "%", "[", "]", "(", ")", "{", "}", ".", "~", "?", ":", ",", ";", "#", "$"
    };

    private readonly SourceText _source;
    private readonly string _text;
    private readonly int _end;
    private readonly List<Diagnostic> _diagnostics;
    private int _position;
    private bool _expectOperand = true;

    public Lexer(SourceText source, string text, List<Diagnostic> diagnostics, int start = 0, int? end = null)
    {
        _source = source; _text = text; _diagnostics = diagnostics; _position = start; _end = end ?? text.Length;
    }

    public IReadOnlyList<Token> Lex()
    {
        var tokens = new List<Token>();
        while (true)
        {
            SkipTrivia();
            if (_position >= _end) { tokens.Add(new Token(TokenKind.End, string.Empty, _end, _end)); break; }
            var token = NextToken();
            tokens.Add(token);
            UpdateOperandState(token);
        }
        return tokens;
    }

    public static IReadOnlyList<CommentTrivia> CollectComments(SourceText source)
    {
        var result = new List<CommentTrivia>();
        var text = source.Text;
        var i = 0;
        while (i < text.Length)
        {
            if (text[i] == '\'' || text[i] == '"') { i = SkipQuoted(text, i, text[i]); continue; }
            if (text[i] == '@')
            {
                var q = i + 1;
                while (q < text.Length && char.IsWhiteSpace(text[q])) q++;
                if (q < text.Length && (text[q] == '\'' || text[q] == '"')) { i = SkipInterpolated(text, q, text[q]); continue; }
            }
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '/')
            {
                var end = text.IndexOf('\n', i + 2);
                if (end < 0) end = text.Length;
                result.Add(new CommentTrivia(CommentKind.Line, text.Substring(i, end - i), source.Span(i, end)));
                i = end; continue;
            }
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*')
            {
                var end = SkipBlockComment(text, i);
                result.Add(new CommentTrivia(CommentKind.Block, text.Substring(i, end - i), source.Span(i, end)));
                i = end; continue;
            }
            i++;
        }
        return result;
    }

    private Token NextToken()
    {
        var start = _position;
        var ch = _text[_position];
        if (IsIdentifierStart(ch)) return ScanIdentifier();
        if (char.IsDigit(ch) || (ch == '.' && _position + 1 < _end && char.IsDigit(_text[_position + 1]))) return ScanNumber();
        if (ch == '\'' || ch == '"') return ScanString();
        if (ch == '@')
        {
            var q = _position + 1;
            while (q < _end && char.IsWhiteSpace(_text[q])) q++;
            if (q < _end && (_text[q] == '\'' || _text[q] == '"')) return ScanInterpolated(q);
        }
        if (_expectOperand && ch == '<' && _position + 1 < _end && _text[_position + 1] == '%') return ScanOctet();
        if (_expectOperand && ch == '/') return ScanRegExp();
        foreach (var symbol in Symbols)
        {
            if (Matches(symbol)) { _position += symbol.Length; return new Token(TokenKind.Symbol, symbol, start, _position); }
        }
        _position++;
        _diagnostics.Add(new Diagnostic("TJS2001", DiagnosticSeverity.Error, "Invalid character '" + ch + "'.", _source.Span(start, _position)));
        return new Token(TokenKind.Invalid, ch.ToString(), start, _position);
    }

    private Token ScanIdentifier()
    {
        var start = _position++;
        while (_position < _end && IsIdentifierPart(_text[_position])) _position++;
        return new Token(TokenKind.Identifier, _text.Substring(start, _position - start), start, _position);
    }

    private Token ScanNumber()
    {
        var start = _position;
        var exponentSeen = false;
        var prefix = '\0';
        if (_position + 1 < _end && _text[_position] == '0' && (_text[_position + 1] == 'x' || _text[_position + 1] == 'X' || _text[_position + 1] == 'b' || _text[_position + 1] == 'B'))
        { prefix = char.ToLowerInvariant(_text[_position + 1]); _position += 2; }
        else _position++;
        while (_position < _end)
        {
            var ch = _text[_position];
            if (char.IsDigit(ch) || (prefix == 'x' && Uri.IsHexDigit(ch)) || ch == '.') { _position++; continue; }
            if (!exponentSeen && ((prefix == '\0' && (ch == 'e' || ch == 'E')) || (prefix != '\0' && (ch == 'p' || ch == 'P'))))
            {
                exponentSeen = true; _position++;
                if (_position < _end && (_text[_position] == '+' || _text[_position] == '-')) _position++;
                continue;
            }
            break;
        }
        return new Token(TokenKind.Number, _text.Substring(start, _position - start), start, _position);
    }

    private Token ScanString()
    {
        var start = _position;
        var delimiter = _text[_position++];
        var value = new StringBuilder();
        while (true)
        {
            var closed = false;
            while (_position < _end)
            {
                var ch = _text[_position++];
                if (ch == '\\') { AppendEscape(value); continue; }
                if (ch == delimiter) { closed = true; break; }
                value.Append(ch);
            }
            if (!closed)
            {
                _diagnostics.Add(new Diagnostic("TJS2002", DiagnosticSeverity.Error, "Unclosed string literal.", _source.Span(start, _position)));
                break;
            }
            var look = _position;
            while (look < _end && char.IsWhiteSpace(_text[look])) look++;
            if (look < _end && _text[look] == delimiter) { _position = look + 1; continue; }
            break;
        }
        return new Token(TokenKind.String, _text.Substring(start, _position - start), start, _position, value.ToString());
    }

    private Token ScanInterpolated(int quoteAt)
    {
        var start = _position;
        var delimiter = _text[quoteAt];
        _position = quoteAt + 1;
        var parts = new List<InterpolatedTokenPart>();
        var textStart = _position;
        var decoded = new StringBuilder();
        var closed = false;
        while (_position < _end)
        {
            var ch = _text[_position];
            if (ch == '\\')
            {
                _position++;
                AppendEscape(decoded);
                continue;
            }
            if (ch == delimiter)
            {
                AddTextPart(parts, textStart, _position, decoded.ToString());
                _position++; closed = true; break;
            }
            if (ch == '$' && _position + 1 < _end && _text[_position + 1] == '{')
            {
                AddTextPart(parts, textStart, _position, decoded.ToString());
                var marker = _position;
                var expressionStart = _position + 2;
                var close = FindExpressionEnd(expressionStart, '}');
                if (close < 0) { _position = _end; break; }
                parts.Add(new InterpolatedTokenPart(true, expressionStart, close, "${}", _text.Substring(expressionStart, close - expressionStart)));
                _position = close + 1; textStart = _position; decoded.Clear();
                continue;
            }
            if (ch == '&')
            {
                AddTextPart(parts, textStart, _position, decoded.ToString());
                var expressionStart = _position + 1;
                var close = FindExpressionEnd(expressionStart, ';');
                if (close < 0) { _position = _end; break; }
                parts.Add(new InterpolatedTokenPart(true, expressionStart, close, "&;", _text.Substring(expressionStart, close - expressionStart)));
                _position = close + 1; textStart = _position; decoded.Clear();
                continue;
            }
            decoded.Append(ch); _position++;
        }
        if (!closed) _diagnostics.Add(new Diagnostic("TJS2003", DiagnosticSeverity.Error, "Unclosed interpolated string.", _source.Span(start, _position)));
        return new Token(TokenKind.InterpolatedString, _text.Substring(start, _position - start), start, _position, new InterpolatedTokenData(parts));
    }

    private void AddTextPart(List<InterpolatedTokenPart> parts, int start, int end, string value)
    {
        if (end > start || value.Length != 0) parts.Add(new InterpolatedTokenPart(false, start, end, string.Empty, value));
    }

    private int FindExpressionEnd(int start, char terminator)
    {
        var i = start;
        var paren = 0; var bracket = 0; var brace = 0;
        while (i < _end)
        {
            var ch = _text[i];
            if (ch == '\'' || ch == '"') { i = SkipQuoted(_text, i, ch); continue; }
            if (i + 1 < _end && ch == '/' && _text[i + 1] == '*') { i = SkipBlockComment(_text, i); continue; }
            if (i + 1 < _end && ch == '/' && _text[i + 1] == '/') { var nl = _text.IndexOf('\n', i + 2); i = nl < 0 ? _end : nl; continue; }
            if (ch == '(') paren++; else if (ch == ')') paren--;
            else if (ch == '[') bracket++; else if (ch == ']') bracket--;
            else if (ch == '{') brace++; else if (ch == '}') { if (terminator == '}' && paren == 0 && bracket == 0 && brace == 0) return i; brace--; }
            if (ch == terminator && terminator != '}' && paren == 0 && bracket == 0 && brace == 0) return i;
            i++;
        }
        return -1;
    }

    private Token ScanRegExp()
    {
        var start = _position++;
        var patternStart = _position;
        var escaped = false; var inClass = false;
        while (_position < _end)
        {
            var ch = _text[_position];
            if (!escaped)
            {
                if (ch == '[') inClass = true; else if (ch == ']') inClass = false;
                else if (ch == '/' && !inClass) break;
            }
            escaped = !escaped && ch == '\\';
            if (ch != '\\') escaped = false;
            _position++;
        }
        if (_position >= _end)
        {
            _diagnostics.Add(new Diagnostic("TJS2004", DiagnosticSeverity.Error, "Unclosed regular expression literal.", _source.Span(start, _position)));
            return new Token(TokenKind.RegExp, _text.Substring(start, _position - start), start, _position, new RegExpTokenData(_text.Substring(patternStart), string.Empty));
        }
        var pattern = _text.Substring(patternStart, _position - patternStart);
        _position++;
        var flagsStart = _position;
        while (_position < _end && (_text[_position] == 'g' || _text[_position] == 'i' || _text[_position] == 'l')) _position++;
        return new Token(TokenKind.RegExp, _text.Substring(start, _position - start), start, _position, new RegExpTokenData(pattern, _text.Substring(flagsStart, _position - flagsStart)));
    }

    private Token ScanOctet()
    {
        var start = _position; _position += 2;
        var hex = new StringBuilder();
        var closed = false;
        while (_position < _end)
        {
            if (_position + 1 < _end && _text[_position] == '%' && _text[_position + 1] == '>') { _position += 2; closed = true; break; }
            var ch = _text[_position++];
            if (Uri.IsHexDigit(ch)) hex.Append(char.ToUpperInvariant(ch));
            else if (!char.IsWhiteSpace(ch)) _diagnostics.Add(new Diagnostic("TJS2005", DiagnosticSeverity.Error, "Invalid octet literal character.", _source.Span(_position - 1, _position)));
        }
        if (!closed) _diagnostics.Add(new Diagnostic("TJS2006", DiagnosticSeverity.Error, "Unclosed octet literal.", _source.Span(start, _position)));
        if ((hex.Length & 1) != 0) _diagnostics.Add(new Diagnostic("TJS2007", DiagnosticSeverity.Error, "Octet literal must contain pairs of hexadecimal digits.", _source.Span(start, _position)));
        return new Token(TokenKind.Octet, _text.Substring(start, _position - start), start, _position, hex.ToString());
    }

    private void AppendEscape(StringBuilder builder)
    {
        if (_position >= _end) return;
        var ch = _text[_position++];
        if (ch == 'x' || ch == 'X')
        {
            var code = 0; var count = 0;
            while (_position < _end && count < 4 && Uri.IsHexDigit(_text[_position])) { code = code * 16 + HexValue(_text[_position++]); count++; }
            builder.Append((char)code); return;
        }
        if (ch == '0')
        {
            var code = 0;
            while (_position < _end && _text[_position] >= '0' && _text[_position] <= '7') { code = code * 8 + (_text[_position++] - '0'); }
            builder.Append((char)code); return;
        }
        switch (ch)
        {
            case 'a': builder.Append('\a'); break; case 'b': builder.Append('\b'); break; case 'f': builder.Append('\f'); break;
            case 'n': builder.Append('\n'); break; case 'r': builder.Append('\r'); break; case 't': builder.Append('\t'); break; case 'v': builder.Append('\v'); break;
            default: builder.Append(ch); break;
        }
    }

    private void SkipTrivia()
    {
        while (_position < _end)
        {
            if (char.IsWhiteSpace(_text[_position])) { _position++; continue; }
            if (_position + 1 < _end && _text[_position] == '/' && _text[_position + 1] == '/')
            { var nl = _text.IndexOf('\n', _position + 2); _position = nl < 0 || nl >= _end ? _end : nl + 1; continue; }
            if (_position + 1 < _end && _text[_position] == '/' && _text[_position + 1] == '*')
            { _position = Math.Min(_end, SkipBlockComment(_text, _position)); continue; }
            break;
        }
    }

    private void UpdateOperandState(Token token)
    {
        if (token.Kind == TokenKind.Number || token.Kind == TokenKind.String || token.Kind == TokenKind.InterpolatedString || token.Kind == TokenKind.RegExp || token.Kind == TokenKind.Octet)
        { _expectOperand = false; return; }
        if (token.Kind == TokenKind.Identifier)
        {
            switch (token.Text)
            {
                case "return": case "throw": case "case": case "delete": case "typeof": case "new": case "invalidate": case "isvalid":
                case "instanceof": case "incontextof": case "if": case "else": case "var": case "const": case "int": case "real": case "string":
                    _expectOperand = true; return;
                default: _expectOperand = false; return;
            }
        }
        if (token.Kind == TokenKind.Symbol)
        {
            _expectOperand = token.Text != ")" && token.Text != "]" && token.Text != "++" && token.Text != "--";
            if (token.Text == "}" ) _expectOperand = false;
        }
    }

    private bool Matches(string value) => _position + value.Length <= _end && string.CompareOrdinal(_text, _position, value, 0, value.Length) == 0;
    private static bool IsIdentifierStart(char ch) => ch == '_' || char.IsLetter(ch) || ch >= 0x80;
    private static bool IsIdentifierPart(char ch) => ch == '_' || char.IsLetterOrDigit(ch) || ch >= 0x80;
    private static int HexValue(char ch) => ch >= '0' && ch <= '9' ? ch - '0' : char.ToLowerInvariant(ch) - 'a' + 10;

    private static int SkipQuoted(string text, int start, char delimiter)
    {
        var i = start + 1;
        while (i < text.Length) { if (text[i] == '\\') i += 2; else if (text[i++] == delimiter) break; else { } }
        return Math.Min(i, text.Length);
    }

    private static int SkipInterpolated(string text, int quoteAt, char delimiter) => SkipQuoted(text, quoteAt, delimiter);

    private static int SkipBlockComment(string text, int start)
    {
        var i = start + 2; var depth = 1;
        while (i < text.Length && depth != 0)
        {
            if (i + 1 < text.Length && text[i] == '/' && text[i + 1] == '*') { depth++; i += 2; }
            else if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '/') { depth--; i += 2; }
            else i++;
        }
        return i;
    }
}
