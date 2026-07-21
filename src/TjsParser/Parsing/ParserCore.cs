using System;
using System.Collections.Generic;
using System.Globalization;
using TjsParser.Syntax;

namespace TjsParser.Parsing;

internal sealed class ParserCore
{
    private static readonly HashSet<string> AssignmentOperators = new HashSet<string>(StringComparer.Ordinal)
    { "=", "<->", "&=", "|=", "^=", "-=", "+=", "%=", "/=", "\\=", "*=", "||=", "&&=", ">>=", "<<=", ">>>=" };

    private readonly SourceText _source;
    private readonly string _processedText;
    private readonly IReadOnlyList<Token> _tokens;
    private readonly List<Diagnostic> _diagnostics;
    private readonly ParseOptions _options;
    private int _position;

    public ParserCore(SourceText source, string processedText, IReadOnlyList<Token> tokens, List<Diagnostic> diagnostics, ParseOptions options)
    { _source = source; _processedText = processedText; _tokens = tokens; _diagnostics = diagnostics; _options = options; }

    public RootMode SelectedRootMode { get; private set; }

    public DocumentSyntax ParseDocument()
    {
        var mode = _options.RootMode;
        if (mode == RootMode.Auto) mode = LooksLikeExpressionDocument() ? RootMode.Expression : RootMode.Script;
        SelectedRootMode = mode;
        if (mode == RootMode.Expression)
        {
            var expression = ParseExpression();
            if (!IsEnd) Report("TJS3001", "Unexpected token after the document expression.", Current);
            return new ExpressionDocumentSyntax(_source.Span(0, LastConsumedEnd()), expression);
        }
        var body = new List<StatementSyntax>();
        while (!IsEnd)
        {
            var start = _position;
            body.Add(ParseStatement());
            if (_position == start) { Report("TJS3002", "Parser could not make progress.", Current); Next(); }
        }
        return new ScriptDocumentSyntax(_source.Span(0, _source.Length), body);
    }

    private bool LooksLikeExpressionDocument()
    {
        if (At("%") && Peek(1).Text == "[") return true;
        if (At("[") ) return true;
        return At("(") && Peek(1).Text == "const" && Peek(2).Text == ")" && (Peek(3).Text == "[" || Peek(3).Text == "%");
    }

    private StatementSyntax ParseStatement()
    {
        if (At(";")) { var token = Next(); return new EmptyStatementSyntax(Span(token.Start, token.End)); }
        if (At("{")) return ParseBlock();
        if (At("if")) return ParseIf();
        if (At("while")) return ParseWhile();
        if (At("do")) return ParseDoWhile();
        if (At("for")) return ParseFor();
        if (At("break")) return ParseKeywordStatement(SyntaxKind.BreakStatement);
        if (At("continue")) return ParseKeywordStatement(SyntaxKind.ContinueStatement);
        if (At("debugger")) return ParseKeywordStatement(SyntaxKind.DebuggerStatement);
        if (At("var") || At("const")) return ParseVariableDeclaration(true);
        if (At("function")) return ParseFunctionDeclaration();
        if (At("property")) return ParsePropertyDeclaration();
        if (At("class")) return ParseClassDeclaration();
        if (At("return")) return ParseReturn();
        if (At("switch")) return ParseSwitch();
        if (At("with")) return ParseWith();
        if (At("case") || At("default")) return ParseCaseLabel();
        if (At("try")) return ParseTry();
        if (At("throw")) return ParseThrow();
        if (At("else") || At("catch"))
        {
            var bad = Next(); Report("TJS3003", "Unexpected '" + bad.Text + "'.", bad);
            return new ErrorStatementSyntax(Span(bad.Start, bad.End), "Unexpected " + bad.Text);
        }

        var start = Current.Start;
        var expression = ParseExpression();
        var end = expression.Span.End.Offset;
        if (Take(";", out var semicolon)) end = semicolon.End;
        else
        {
            Report("TJS3004", "Expected ';' after expression.", Current);
            RecoverStatement();
            end = LastConsumedEnd();
        }
        return new ExpressionStatementSyntax(Span(start, end), expression);
    }

    private BlockStatementSyntax ParseBlock()
    {
        var open = Expect("{");
        var statements = new List<StatementSyntax>();
        while (!IsEnd && !At("}"))
        {
            var before = _position;
            statements.Add(ParseStatement());
            if (_position == before) Next();
        }
        var close = Expect("}");
        return new BlockStatementSyntax(Span(open.Start, close.End), statements);
    }

    private StatementSyntax ParseIf()
    {
        var start = Next().Start;
        Expect("("); var condition = ParseExpression(); Expect(")");
        var thenStatement = ParseStatement();
        StatementSyntax? elseStatement = null;
        if (Take("else", out _)) elseStatement = ParseStatement();
        return new IfStatementSyntax(Span(start, elseStatement?.Span.End.Offset ?? thenStatement.Span.End.Offset), condition, thenStatement, elseStatement);
    }

    private StatementSyntax ParseWhile()
    {
        var start = Next().Start; Expect("("); var condition = ParseExpression(); Expect(")"); var body = ParseStatement();
        return new WhileStatementSyntax(Span(start, body.Span.End.Offset), condition, body);
    }

    private StatementSyntax ParseDoWhile()
    {
        var start = Next().Start; var body = ParseStatement(); Expect("while"); Expect("("); var condition = ParseExpression(); Expect(")"); var end = Expect(";").End;
        return new DoWhileStatementSyntax(Span(start, end), body, condition);
    }

    private StatementSyntax ParseFor()
    {
        var start = Next().Start; Expect("(");
        SyntaxNode? initializer = null;
        if (!At(";")) initializer = At("var") || At("const") ? ParseVariableDeclaration(false) : ParseExpression();
        Expect(";");
        ExpressionSyntax? condition = null; if (!At(";")) condition = ParseExpression(); Expect(";");
        ExpressionSyntax? increment = null; if (!At(")")) increment = ParseExpression(); Expect(")");
        var body = ParseStatement();
        return new ForStatementSyntax(Span(start, body.Span.End.Offset), initializer, condition, increment, body);
    }

    private StatementSyntax ParseKeywordStatement(SyntaxKind kind)
    {
        var start = Next().Start; var end = Expect(";").End; return new KeywordStatementSyntax(kind, Span(start, end));
    }

    private VariableDeclarationSyntax ParseVariableDeclaration(bool consumeSemicolon)
    {
        var keyword = Next();
        var declarations = new List<VariableDeclaratorSyntax>();
        do
        {
            var name = ExpectIdentifier();
            var typeName = ParseOptionalType();
            ExpressionSyntax? initializer = null;
            if (Take("=", out _)) initializer = ParseExpression(2, false);
            declarations.Add(new VariableDeclaratorSyntax(Span(name.Start, initializer?.Span.End.Offset ?? LastConsumedEnd()), name.Text, typeName, initializer));
        } while (Take(",", out _));
        var end = LastConsumedEnd();
        if (consumeSemicolon) end = Expect(";").End;
        return new VariableDeclarationSyntax(Span(keyword.Start, end), keyword.Text == "const", declarations);
    }

    private FunctionDeclarationSyntax ParseFunctionDeclaration()
    {
        var start = Next().Start;
        var name = ExpectIdentifier();
        var parameters = ParseParameters();
        var returnType = ParseOptionalType();
        var body = ParseBlock();
        return new FunctionDeclarationSyntax(Span(start, body.Span.End.Offset), name.Text, parameters, returnType, body);
    }

    private IReadOnlyList<ParameterSyntax> ParseParameters()
    {
        var parameters = new List<ParameterSyntax>();
        if (!Take("(", out _)) return parameters;
        if (Take(")", out _)) return parameters;
        while (!IsEnd && !At(")"))
        {
            var start = Current.Start;
            if (Take("*", out var star))
            {
                parameters.Add(new ParameterSyntax(Span(start, star.End), null, null, null, true));
            }
            else
            {
                var name = ExpectIdentifier();
                var type = ParseOptionalType();
                var collapse = Take("*", out _);
                ExpressionSyntax? defaultValue = null;
                if (!collapse && Take("=", out _)) defaultValue = ParseExpression(2, false);
                parameters.Add(new ParameterSyntax(Span(start, defaultValue?.Span.End.Offset ?? LastConsumedEnd()), name.Text, type, defaultValue, collapse));
            }
            if (!Take(",", out _)) break;
        }
        Expect(")");
        return parameters;
    }

    private string? ParseOptionalType()
    {
        if (!Take(":", out _)) return null;
        return ExpectIdentifier().Text;
    }

    private PropertyDeclarationSyntax ParsePropertyDeclaration()
    {
        var start = Next().Start; var name = ExpectIdentifier(); Expect("{");
        var accessors = new List<PropertyAccessorSyntax>();
        while (!IsEnd && !At("}"))
        {
            if (At("getter"))
            {
                var astart = Next().Start;
                if (Take("(", out _)) Expect(")");
                var type = ParseOptionalType(); var body = ParseBlock();
                accessors.Add(new PropertyAccessorSyntax(Span(astart, body.Span.End.Offset), "getter", null, type, body));
            }
            else if (At("setter"))
            {
                var astart = Next().Start; Expect("("); var parameter = ExpectIdentifier(); var type = ParseOptionalType(); Expect(")"); var body = ParseBlock();
                accessors.Add(new PropertyAccessorSyntax(Span(astart, body.Span.End.Offset), "setter", parameter.Text, type, body));
            }
            else
            {
                Report("TJS3005", "Expected getter or setter.", Current); Next();
            }
        }
        var close = Expect("}");
        return new PropertyDeclarationSyntax(Span(start, close.End), name.Text, accessors);
    }

    private ClassDeclarationSyntax ParseClassDeclaration()
    {
        var start = Next().Start; var name = ExpectIdentifier();
        var bases = new List<ExpressionSyntax>();
        if (Take("extends", out _))
        {
            do { bases.Add(ParseExpression(2, false)); } while (Take(",", out _));
        }
        var body = ParseBlock();
        return new ClassDeclarationSyntax(Span(start, body.Span.End.Offset), name.Text, bases, body);
    }

    private ReturnStatementSyntax ParseReturn()
    {
        var start = Next().Start; ExpressionSyntax? expression = null;
        if (!At(";")) expression = ParseExpression();
        var end = Expect(";").End; return new ReturnStatementSyntax(Span(start, end), expression);
    }

    private SwitchStatementSyntax ParseSwitch()
    {
        var start = Next().Start; Expect("("); var expression = ParseExpression(); Expect(")"); var body = ParseBlock();
        return new SwitchStatementSyntax(Span(start, body.Span.End.Offset), expression, body);
    }

    private WithStatementSyntax ParseWith()
    {
        var start = Next().Start; Expect("("); var expression = ParseExpression(); Expect(")"); var body = ParseStatement();
        return new WithStatementSyntax(Span(start, body.Span.End.Offset), expression, body);
    }

    private CaseLabelSyntax ParseCaseLabel()
    {
        var token = Next(); ExpressionSyntax? value = null;
        if (token.Text == "case") value = ParseExpression();
        var end = Expect(":").End; return new CaseLabelSyntax(Span(token.Start, end), value, token.Text == "default");
    }

    private TryStatementSyntax ParseTry()
    {
        var start = Next().Start; var tryBody = ParseStatement(); Expect("catch");
        string? variable = null;
        if (Take("(", out _)) { if (!At(")")) variable = ExpectIdentifier().Text; Expect(")"); }
        var catchBody = ParseStatement();
        return new TryStatementSyntax(Span(start, catchBody.Span.End.Offset), tryBody, variable, catchBody);
    }

    private ThrowStatementSyntax ParseThrow()
    {
        var start = Next().Start; var expression = ParseExpression(); var end = Expect(";").End;
        return new ThrowStatementSyntax(Span(start, end), expression);
    }

    private ExpressionSyntax ParseExpression(int minPrecedence = 0, bool allowComma = true, bool stopAtExpansion = false)
    {
        var left = ParsePrefix();
        left = ParsePostfix(left);

        while (!IsEnd)
        {
            if (At("?") && 3 >= minPrecedence)
            {
                Next(); var whenTrue = ParseExpression(); Expect(":"); var whenFalse = ParseExpression(3);
                left = new ConditionalExpressionSyntax(Span(left.Span.Start.Offset, whenFalse.Span.End.Offset), left, whenTrue, whenFalse);
                continue;
            }

            var op = Current.Text;
            if (stopAtExpansion && op == "*" && (Peek(1).Text == "," || Peek(1).Text == ")")) break;
            var precedence = BinaryPrecedence(op, allowComma);
            if (precedence < minPrecedence) break;
            Next();
            var rightAssociative = AssignmentOperators.Contains(op);
            var right = ParseExpression(rightAssociative ? precedence : precedence + 1, allowComma, stopAtExpansion);
            left = new BinaryExpressionSyntax(Span(left.Span.Start.Offset, right.Span.End.Offset), op, left, right);
        }

        if (minPrecedence == 0 && At("if"))
        {
            Next(); var condition = ParseExpression();
            left = new PostfixIfExpressionSyntax(Span(left.Span.Start.Offset, condition.Span.End.Offset), left, condition);
        }
        return left;
    }

    private ExpressionSyntax ParsePrefix()
    {
        var token = Current;
        if (At("!") || At("~") || At("--") || At("++") || At("invalidate") || At("delete") || At("typeof") || At("#") || At("$") || At("+") || At("-") || At("&") || At("*"))
        {
            Next(); var operand = ParseExpression(15, false); return new UnaryExpressionSyntax(Span(token.Start, operand.Span.End.Offset), token.Text, operand);
        }
        if (At("isvalid")) { Next(); var operand = ParseExpression(15, false); return new UnaryExpressionSyntax(Span(token.Start, operand.Span.End.Offset), "isvalid", operand); }
        if (At("new")) { Next(); var operand = ParseExpression(15, false); return new NewExpressionSyntax(Span(token.Start, operand.Span.End.Offset), operand); }
        if (At("int") || At("real") || At("string"))
        {
            Next(); var operand = ParseExpression(15, false); return new UnaryExpressionSyntax(Span(token.Start, operand.Span.End.Offset), token.Text, operand);
        }
        if (At("function")) return ParseFunctionExpression();
        if (At("[")) return ParseArray(false);
        if (At("%") && Peek(1).Text == "[") return ParseDictionary(false);
        if (At("("))
        {
            if (Peek(1).Text == "const" && Peek(2).Text == ")" && Peek(3).Text == "[") { Next(); Next(); Next(); return ParseArray(true); }
            if (Peek(1).Text == "const" && Peek(2).Text == ")" && Peek(3).Text == "%") { Next(); Next(); Next(); return ParseDictionary(true); }
            if ((Peek(1).Text == "int" || Peek(1).Text == "real" || Peek(1).Text == "string") && Peek(2).Text == ")")
            {
                var start = Next().Start; var cast = Next().Text; Next(); var operand = ParseExpression(15, false);
                return new UnaryExpressionSyntax(Span(start, operand.Span.End.Offset), cast, operand);
            }
            Next(); var expression = ParseExpression(); Expect(")"); return expression;
        }
        if (At(".") && Peek(1).Kind == TokenKind.Identifier)
        {
            var start = Next().Start; var member = Next(); return new WithMemberExpressionSyntax(Span(start, member.End), member.Text);
        }
        if (token.Kind == TokenKind.Number) { Next(); return ParseNumberLiteral(token); }
        if (token.Kind == TokenKind.String) { Next(); return new LiteralExpressionSyntax(Span(token.Start, token.End), LiteralKind.String, token.Value, token.Text); }
        if (token.Kind == TokenKind.InterpolatedString) { Next(); return ParseInterpolated(token); }
        if (token.Kind == TokenKind.RegExp)
        {
            Next(); var data = (RegExpTokenData)token.Value!; return new RegExpLiteralSyntax(Span(token.Start, token.End), data.Pattern, data.Flags, token.Text);
        }
        if (token.Kind == TokenKind.Octet) { Next(); return new OctetLiteralSyntax(Span(token.Start, token.End), (string)token.Value!, token.Text); }
        if (token.Kind == TokenKind.Identifier)
        {
            Next();
            switch (token.Text)
            {
                case "this": return new NameExpressionSyntax(SyntaxKind.ThisExpression, Span(token.Start, token.End));
                case "super": return new NameExpressionSyntax(SyntaxKind.SuperExpression, Span(token.Start, token.End));
                case "global": return new NameExpressionSyntax(SyntaxKind.GlobalExpression, Span(token.Start, token.End));
                case "void": return new NameExpressionSyntax(SyntaxKind.VoidExpression, Span(token.Start, token.End));
                case "true": return new LiteralExpressionSyntax(Span(token.Start, token.End), LiteralKind.Boolean, true, token.Text);
                case "false": return new LiteralExpressionSyntax(Span(token.Start, token.End), LiteralKind.Boolean, false, token.Text);
                case "null": return new LiteralExpressionSyntax(Span(token.Start, token.End), LiteralKind.Null, null, token.Text);
                case "NaN": return new LiteralExpressionSyntax(Span(token.Start, token.End), LiteralKind.NaN, "NaN", token.Text);
                case "Infinity": return new LiteralExpressionSyntax(Span(token.Start, token.End), LiteralKind.Infinity, "Infinity", token.Text);
                default: return new NameExpressionSyntax(SyntaxKind.IdentifierExpression, Span(token.Start, token.End), token.Text);
            }
        }
        Report("TJS3006", "Expected expression.", token);
        if (!IsEnd) Next();
        return new ErrorExpressionSyntax(Span(token.Start, token.End), "Expected expression");
    }

    private ExpressionSyntax ParsePostfix(ExpressionSyntax expression)
    {
        while (!IsEnd)
        {
            if (Take("[", out _))
            {
                var index = ParseExpression(); var close = Expect("]"); expression = new IndexExpressionSyntax(Span(expression.Span.Start.Offset, close.End), expression, index); continue;
            }
            if (Take(".", out _))
            {
                var member = ExpectIdentifier(); expression = new MemberExpressionSyntax(Span(expression.Span.Start.Offset, member.End), expression, member.Text); continue;
            }
            if (At("(")) { expression = ParseCall(expression); continue; }
            if (At("++") || At("--") || At("!"))
            {
                var op = Next(); expression = new UnaryExpressionSyntax(Span(expression.Span.Start.Offset, op.End), op.Text, expression, true); continue;
            }
            if (At("isvalid"))
            {
                var op = Next(); expression = new UnaryExpressionSyntax(Span(expression.Span.Start.Offset, op.End), "isvalid", expression, true); continue;
            }
            break;
        }
        return expression;
    }

    private CallExpressionSyntax ParseCall(ExpressionSyntax target)
    {
        Next(); var arguments = new List<ArgumentSyntax>();
        if (!At(")"))
        {
            while (!IsEnd && !At(")"))
            {
                var start = Current.Start;
                if (Take("...", out var omit)) arguments.Add(new ArgumentSyntax(Span(start, omit.End), null, false, true));
                else if (At(",")) arguments.Add(new ArgumentSyntax(Span(start, start), null, false, false));
                else if (Take("*", out var star)) arguments.Add(new ArgumentSyntax(Span(start, star.End), null, true, false));
                else
                {
                    var value = ParseExpression(2, false, true);
                    var expanded = Take("*", out var expand);
                    arguments.Add(new ArgumentSyntax(Span(start, expanded ? expand.End : value.Span.End.Offset), value, expanded, false));
                }
                if (!Take(",", out _)) break;
                if (At(")")) arguments.Add(new ArgumentSyntax(Span(Current.Start, Current.Start), null, false, false));
            }
        }
        var close = Expect(")"); return new CallExpressionSyntax(Span(target.Span.Start.Offset, close.End), target, arguments);
    }

    private FunctionExpressionSyntax ParseFunctionExpression()
    {
        var start = Next().Start; var parameters = ParseParameters(); var returnType = ParseOptionalType(); var body = ParseBlock();
        return new FunctionExpressionSyntax(Span(start, body.Span.End.Offset), parameters, returnType, body);
    }

    private ArrayExpressionSyntax ParseArray(bool isConst)
    {
        var start = Expect("[").Start; var elements = new List<ArrayElementSyntax>();
        while (!IsEnd && !At("]"))
        {
            var elementStart = Current.Start;
            if (At(",")) elements.Add(new ArrayElementSyntax(Span(elementStart, elementStart), null));
            else
            {
                var value = ParseExpression(2, false); elements.Add(new ArrayElementSyntax(value.Span, value));
            }
            if (!Take(",", out _) && !Take("=>", out _)) break;
            if (At("]")) elements.Add(new ArrayElementSyntax(Span(Current.Start, Current.Start), null));
        }
        var close = Expect("]"); return new ArrayExpressionSyntax(Span(start, close.End), elements, isConst);
    }

    private DictionaryExpressionSyntax ParseDictionary(bool isConst)
    {
        var start = Expect("%").Start; Expect("["); var entries = new List<DictionaryEntrySyntax>();
        while (!IsEnd && !At("]"))
        {
            var entryStart = Current.Start;
            ExpressionSyntax key;
            string separator;
            if (Current.Kind == TokenKind.Identifier && Peek(1).Text == ":")
            {
                var name = Next(); key = new LiteralExpressionSyntax(Span(name.Start, name.End), LiteralKind.String, name.Text, name.Text); Next(); separator = "Colon";
            }
            else
            {
                key = ParseExpression(2, false);
                if (Take("=>", out _)) separator = "Arrow";
                else { Expect(","); separator = "CommaPair"; }
            }
            var value = ParseExpression(2, false);
            entries.Add(new DictionaryEntrySyntax(Span(entryStart, value.Span.End.Offset), key, value, separator));
            if (!Take(",", out _)) break;
        }
        var close = Expect("]"); return new DictionaryExpressionSyntax(Span(start, close.End), entries, isConst);
    }

    private InterpolatedStringExpressionSyntax ParseInterpolated(Token token)
    {
        var parts = new List<InterpolatedPartSyntax>();
        foreach (var part in ((InterpolatedTokenData)token.Value!).Parts)
        {
            if (!part.IsExpression) parts.Add(new InterpolatedTextSyntax(Span(part.Start, part.End), part.Text, _source.Text.Substring(part.Start, part.End - part.Start)));
            else
            {
                var lexer = new Lexer(_source, _processedText, _diagnostics, part.Start, part.End);
                var nested = new ParserCore(_source, _processedText, lexer.Lex(), _diagnostics, _options);
                var expression = nested.ParseExpression();
                parts.Add(new InterpolationSyntax(Span(part.Start, part.End), part.Delimiter, expression));
            }
        }
        return new InterpolatedStringExpressionSyntax(Span(token.Start, token.End), parts, token.Text);
    }

    private LiteralExpressionSyntax ParseNumberLiteral(Token token)
    {
        var raw = token.Text;
        var isReal = raw.IndexOf('.') >= 0 || raw.IndexOf('e') >= 0 || raw.IndexOf('E') >= 0 || raw.IndexOf('p') >= 0 || raw.IndexOf('P') >= 0;
        object value = raw;
        try
        {
            if (!isReal)
            {
                var signless = raw;
                int radix;
                if (signless.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) { radix = 16; signless = signless.Substring(2); }
                else if (signless.StartsWith("0b", StringComparison.OrdinalIgnoreCase)) { radix = 2; signless = signless.Substring(2); }
                else if (signless.Length > 1 && signless[0] == '0') { radix = 8; signless = signless.Substring(1); }
                else radix = 10;
                value = Convert.ToInt64(signless.Length == 0 ? "0" : signless, radix);
            }
            else if (!raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase) && !raw.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
                value = double.Parse(raw, NumberStyles.Float, CultureInfo.InvariantCulture);
        }
        catch (Exception) { value = raw; }
        return new LiteralExpressionSyntax(Span(token.Start, token.End), isReal ? LiteralKind.Real : LiteralKind.Integer, value, raw);
    }

    private static int BinaryPrecedence(string op, bool allowComma)
    {
        if ((op == "," || op == "=>") && allowComma) return 1;
        if (AssignmentOperators.Contains(op)) return 2;
        switch (op)
        {
            case "||": return 4; case "&&": return 5; case "|": return 6; case "^": return 7; case "&": return 8;
            case "==": case "!=": case "===": case "!==": return 9;
            case "<": case ">": case "<=": case ">=": return 10;
            case ">>": case "<<": case ">>>": return 11;
            case "+": case "-": return 12;
            case "%": case "/": case "\\": case "*": return 13;
            case "incontextof": case "instanceof": return 14;
            default: return -1;
        }
    }

    private void RecoverStatement()
    {
        while (!IsEnd && !At(";") && !At("}")) Next();
        if (At(";")) Next();
    }

    private Token Expect(string text)
    {
        if (At(text)) return Next();
        Report("TJS3007", "Expected '" + text + "' but found '" + Current.Text + "'.", Current);
        return new Token(TokenKind.Symbol, text, Current.Start, Current.Start);
    }

    private Token ExpectIdentifier()
    {
        if (Current.Kind == TokenKind.Identifier) return Next();
        Report("TJS3008", "Expected identifier.", Current);
        var at = Current.Start;
        if (!IsEnd) Next();
        return new Token(TokenKind.Identifier, "<missing>", at, at);
    }

    private bool Take(string text, out Token token)
    {
        if (At(text)) { token = Next(); return true; }
        token = new Token(TokenKind.Symbol, text, Current.Start, Current.Start); return false;
    }

    private bool At(string text) => string.Equals(Current.Text, text, StringComparison.Ordinal);
    private bool IsEnd => Current.Kind == TokenKind.End;
    private Token Current => Peek(0);
    private Token Peek(int offset) => _tokens[Math.Min(_position + offset, _tokens.Count - 1)];
    private Token Next() { var token = Current; if (_position < _tokens.Count - 1) _position++; return token; }
    private int LastConsumedEnd() => _position == 0 ? 0 : _tokens[Math.Min(_position - 1, _tokens.Count - 1)].End;
    private SourceSpan Span(int start, int end) => _source.Span(start, end);

    private void Report(string code, string message, Token token)
    {
        var diagnostic = new Diagnostic(code, DiagnosticSeverity.Error, message, Span(token.Start, token.End));
        _diagnostics.Add(diagnostic);
        if (_options.ErrorMode == ErrorMode.FailFast) throw new TjsParseException(diagnostic);
    }
}
