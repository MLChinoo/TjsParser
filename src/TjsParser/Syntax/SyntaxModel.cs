using System;
using System.Collections.Generic;

namespace TjsParser.Syntax;

public enum SyntaxKind
{
    ScriptDocument,
    ExpressionDocument,
    EmptyStatement,
    BlockStatement,
    ExpressionStatement,
    VariableDeclaration,
    VariableDeclarator,
    FunctionDeclaration,
    FunctionExpression,
    Parameter,
    PropertyDeclaration,
    PropertyAccessor,
    ClassDeclaration,
    IfStatement,
    WhileStatement,
    DoWhileStatement,
    ForStatement,
    BreakStatement,
    ContinueStatement,
    DebuggerStatement,
    ReturnStatement,
    SwitchStatement,
    CaseLabel,
    WithStatement,
    TryStatement,
    ThrowStatement,
    ErrorStatement,
    IdentifierExpression,
    ThisExpression,
    SuperExpression,
    GlobalExpression,
    VoidExpression,
    LiteralExpression,
    RegExpLiteral,
    OctetLiteral,
    InterpolatedStringExpression,
    InterpolatedStringText,
    Interpolation,
    UnaryExpression,
    BinaryExpression,
    ConditionalExpression,
    PostfixIfExpression,
    MemberExpression,
    WithMemberExpression,
    IndexExpression,
    CallExpression,
    NewExpression,
    Argument,
    ArrayExpression,
    ArrayElement,
    ConstArrayExpression,
    DictionaryExpression,
    ConstDictionaryExpression,
    DictionaryEntry,
    ErrorExpression
}

public readonly struct SourcePosition
{
    public SourcePosition(int offset, int line, int column)
    {
        Offset = offset;
        Line = line;
        Column = column;
    }

    public int Offset { get; }
    public int Line { get; }
    public int Column { get; }
}

public readonly struct SourceSpan
{
    public SourceSpan(SourcePosition start, SourcePosition end)
    {
        Start = start;
        End = end;
    }

    public SourcePosition Start { get; }
    public SourcePosition End { get; }
    public int Length => End.Offset - Start.Offset;
}

public abstract class SyntaxNode
{
    protected SyntaxNode(SyntaxKind kind, SourceSpan span)
    {
        Kind = kind;
        Span = span;
    }

    public SyntaxKind Kind { get; }
    public SourceSpan Span { get; }
    public virtual IEnumerable<SyntaxNode> ChildNodes() { yield break; }
}

public abstract class DocumentSyntax : SyntaxNode
{
    protected DocumentSyntax(SyntaxKind kind, SourceSpan span) : base(kind, span) { }
}

public sealed class ScriptDocumentSyntax : DocumentSyntax
{
    internal ScriptDocumentSyntax(SourceSpan span, IReadOnlyList<StatementSyntax> body) : base(SyntaxKind.ScriptDocument, span) => Body = body;
    public IReadOnlyList<StatementSyntax> Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Body) yield return item; }
}

public sealed class ExpressionDocumentSyntax : DocumentSyntax
{
    internal ExpressionDocumentSyntax(SourceSpan span, ExpressionSyntax expression) : base(SyntaxKind.ExpressionDocument, span) => Expression = expression;
    public ExpressionSyntax Expression { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; }
}

public abstract class StatementSyntax : SyntaxNode
{
    protected StatementSyntax(SyntaxKind kind, SourceSpan span) : base(kind, span) { }
}

public sealed class EmptyStatementSyntax : StatementSyntax
{
    internal EmptyStatementSyntax(SourceSpan span) : base(SyntaxKind.EmptyStatement, span) { }
}

public sealed class BlockStatementSyntax : StatementSyntax
{
    internal BlockStatementSyntax(SourceSpan span, IReadOnlyList<StatementSyntax> statements) : base(SyntaxKind.BlockStatement, span) => Statements = statements;
    public IReadOnlyList<StatementSyntax> Statements { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Statements) yield return item; }
}

public sealed class ExpressionStatementSyntax : StatementSyntax
{
    internal ExpressionStatementSyntax(SourceSpan span, ExpressionSyntax expression) : base(SyntaxKind.ExpressionStatement, span) => Expression = expression;
    public ExpressionSyntax Expression { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; }
}

public sealed class VariableDeclarationSyntax : StatementSyntax
{
    internal VariableDeclarationSyntax(SourceSpan span, bool isConst, IReadOnlyList<VariableDeclaratorSyntax> declarations) : base(SyntaxKind.VariableDeclaration, span)
    { IsConst = isConst; Declarations = declarations; }
    public bool IsConst { get; }
    public IReadOnlyList<VariableDeclaratorSyntax> Declarations { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Declarations) yield return item; }
}

public sealed class VariableDeclaratorSyntax : SyntaxNode
{
    internal VariableDeclaratorSyntax(SourceSpan span, string name, string? typeName, ExpressionSyntax? initializer) : base(SyntaxKind.VariableDeclarator, span)
    { Name = name; TypeName = typeName; Initializer = initializer; }
    public string Name { get; }
    public string? TypeName { get; }
    public ExpressionSyntax? Initializer { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { if (Initializer != null) yield return Initializer; }
}

public sealed class ParameterSyntax : SyntaxNode
{
    internal ParameterSyntax(SourceSpan span, string? name, string? typeName, ExpressionSyntax? defaultValue, bool isCollapse) : base(SyntaxKind.Parameter, span)
    { Name = name; TypeName = typeName; DefaultValue = defaultValue; IsCollapse = isCollapse; }
    public string? Name { get; }
    public string? TypeName { get; }
    public ExpressionSyntax? DefaultValue { get; }
    public bool IsCollapse { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { if (DefaultValue != null) yield return DefaultValue; }
}

public sealed class FunctionDeclarationSyntax : StatementSyntax
{
    internal FunctionDeclarationSyntax(SourceSpan span, string name, IReadOnlyList<ParameterSyntax> parameters, string? returnType, BlockStatementSyntax body)
        : base(SyntaxKind.FunctionDeclaration, span) { Name = name; Parameters = parameters; ReturnType = returnType; Body = body; }
    public string Name { get; }
    public IReadOnlyList<ParameterSyntax> Parameters { get; }
    public string? ReturnType { get; }
    public BlockStatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Parameters) yield return item; yield return Body; }
}

public sealed class PropertyAccessorSyntax : SyntaxNode
{
    internal PropertyAccessorSyntax(SourceSpan span, string accessorKind, string? parameterName, string? typeName, BlockStatementSyntax body)
        : base(SyntaxKind.PropertyAccessor, span) { AccessorKind = accessorKind; ParameterName = parameterName; TypeName = typeName; Body = body; }
    public string AccessorKind { get; }
    public string? ParameterName { get; }
    public string? TypeName { get; }
    public BlockStatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Body; }
}

public sealed class PropertyDeclarationSyntax : StatementSyntax
{
    internal PropertyDeclarationSyntax(SourceSpan span, string name, IReadOnlyList<PropertyAccessorSyntax> accessors) : base(SyntaxKind.PropertyDeclaration, span)
    { Name = name; Accessors = accessors; }
    public string Name { get; }
    public IReadOnlyList<PropertyAccessorSyntax> Accessors { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Accessors) yield return item; }
}

public sealed class ClassDeclarationSyntax : StatementSyntax
{
    internal ClassDeclarationSyntax(SourceSpan span, string name, IReadOnlyList<ExpressionSyntax> baseTypes, BlockStatementSyntax body) : base(SyntaxKind.ClassDeclaration, span)
    { Name = name; BaseTypes = baseTypes; Body = body; }
    public string Name { get; }
    public IReadOnlyList<ExpressionSyntax> BaseTypes { get; }
    public BlockStatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in BaseTypes) yield return item; yield return Body; }
}

public sealed class IfStatementSyntax : StatementSyntax
{
    internal IfStatementSyntax(SourceSpan span, ExpressionSyntax condition, StatementSyntax thenStatement, StatementSyntax? elseStatement) : base(SyntaxKind.IfStatement, span)
    { Condition = condition; Then = thenStatement; Else = elseStatement; }
    public ExpressionSyntax Condition { get; }
    public StatementSyntax Then { get; }
    public StatementSyntax? Else { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Condition; yield return Then; if (Else != null) yield return Else; }
}

public sealed class WhileStatementSyntax : StatementSyntax
{
    internal WhileStatementSyntax(SourceSpan span, ExpressionSyntax condition, StatementSyntax body) : base(SyntaxKind.WhileStatement, span) { Condition = condition; Body = body; }
    public ExpressionSyntax Condition { get; }
    public StatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Condition; yield return Body; }
}

public sealed class DoWhileStatementSyntax : StatementSyntax
{
    internal DoWhileStatementSyntax(SourceSpan span, StatementSyntax body, ExpressionSyntax condition) : base(SyntaxKind.DoWhileStatement, span) { Body = body; Condition = condition; }
    public StatementSyntax Body { get; }
    public ExpressionSyntax Condition { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Body; yield return Condition; }
}

public sealed class ForStatementSyntax : StatementSyntax
{
    internal ForStatementSyntax(SourceSpan span, SyntaxNode? initializer, ExpressionSyntax? condition, ExpressionSyntax? increment, StatementSyntax body) : base(SyntaxKind.ForStatement, span)
    { Initializer = initializer; Condition = condition; Increment = increment; Body = body; }
    public SyntaxNode? Initializer { get; }
    public ExpressionSyntax? Condition { get; }
    public ExpressionSyntax? Increment { get; }
    public StatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { if (Initializer != null) yield return Initializer; if (Condition != null) yield return Condition; if (Increment != null) yield return Increment; yield return Body; }
}

public sealed class KeywordStatementSyntax : StatementSyntax
{
    internal KeywordStatementSyntax(SyntaxKind kind, SourceSpan span) : base(kind, span) { }
}

public sealed class ReturnStatementSyntax : StatementSyntax
{
    internal ReturnStatementSyntax(SourceSpan span, ExpressionSyntax? expression) : base(SyntaxKind.ReturnStatement, span) => Expression = expression;
    public ExpressionSyntax? Expression { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { if (Expression != null) yield return Expression; }
}

public sealed class SwitchStatementSyntax : StatementSyntax
{
    internal SwitchStatementSyntax(SourceSpan span, ExpressionSyntax expression, BlockStatementSyntax body) : base(SyntaxKind.SwitchStatement, span) { Expression = expression; Body = body; }
    public ExpressionSyntax Expression { get; }
    public BlockStatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; yield return Body; }
}

public sealed class CaseLabelSyntax : StatementSyntax
{
    internal CaseLabelSyntax(SourceSpan span, ExpressionSyntax? value, bool isDefault) : base(SyntaxKind.CaseLabel, span) { Value = value; IsDefault = isDefault; }
    public ExpressionSyntax? Value { get; }
    public bool IsDefault { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { if (Value != null) yield return Value; }
}

public sealed class WithStatementSyntax : StatementSyntax
{
    internal WithStatementSyntax(SourceSpan span, ExpressionSyntax expression, StatementSyntax body) : base(SyntaxKind.WithStatement, span) { Expression = expression; Body = body; }
    public ExpressionSyntax Expression { get; }
    public StatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; yield return Body; }
}

public sealed class TryStatementSyntax : StatementSyntax
{
    internal TryStatementSyntax(SourceSpan span, StatementSyntax tryBody, string? catchVariable, StatementSyntax catchBody) : base(SyntaxKind.TryStatement, span)
    { TryBody = tryBody; CatchVariable = catchVariable; CatchBody = catchBody; }
    public StatementSyntax TryBody { get; }
    public string? CatchVariable { get; }
    public StatementSyntax CatchBody { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return TryBody; yield return CatchBody; }
}

public sealed class ThrowStatementSyntax : StatementSyntax
{
    internal ThrowStatementSyntax(SourceSpan span, ExpressionSyntax expression) : base(SyntaxKind.ThrowStatement, span) => Expression = expression;
    public ExpressionSyntax Expression { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; }
}

public sealed class ErrorStatementSyntax : StatementSyntax
{
    internal ErrorStatementSyntax(SourceSpan span, string message) : base(SyntaxKind.ErrorStatement, span) => Message = message;
    public string Message { get; }
}

public abstract class ExpressionSyntax : SyntaxNode
{
    protected ExpressionSyntax(SyntaxKind kind, SourceSpan span) : base(kind, span) { }
}

public sealed class NameExpressionSyntax : ExpressionSyntax
{
    internal NameExpressionSyntax(SyntaxKind kind, SourceSpan span, string? name = null) : base(kind, span) => Name = name;
    public string? Name { get; }
}

public enum LiteralKind { Integer, Real, String, Boolean, Null, NaN, Infinity }

public sealed class LiteralExpressionSyntax : ExpressionSyntax
{
    internal LiteralExpressionSyntax(SourceSpan span, LiteralKind literalKind, object? value, string raw) : base(SyntaxKind.LiteralExpression, span)
    { LiteralKind = literalKind; Value = value; Raw = raw; }
    public LiteralKind LiteralKind { get; }
    public object? Value { get; }
    public string Raw { get; }
}

public sealed class RegExpLiteralSyntax : ExpressionSyntax
{
    internal RegExpLiteralSyntax(SourceSpan span, string pattern, string flags, string raw) : base(SyntaxKind.RegExpLiteral, span)
    { Pattern = pattern; Flags = flags; Raw = raw; }
    public string Pattern { get; }
    public string Flags { get; }
    public string Raw { get; }
}

public sealed class OctetLiteralSyntax : ExpressionSyntax
{
    internal OctetLiteralSyntax(SourceSpan span, string hex, string raw) : base(SyntaxKind.OctetLiteral, span) { Hex = hex; Raw = raw; }
    public string Hex { get; }
    public string Raw { get; }
}

public abstract class InterpolatedPartSyntax : SyntaxNode
{
    protected InterpolatedPartSyntax(SyntaxKind kind, SourceSpan span) : base(kind, span) { }
}

public sealed class InterpolatedTextSyntax : InterpolatedPartSyntax
{
    internal InterpolatedTextSyntax(SourceSpan span, string value, string raw) : base(SyntaxKind.InterpolatedStringText, span) { Value = value; Raw = raw; }
    public string Value { get; }
    public string Raw { get; }
}

public sealed class InterpolationSyntax : InterpolatedPartSyntax
{
    internal InterpolationSyntax(SourceSpan span, string delimiter, ExpressionSyntax expression) : base(SyntaxKind.Interpolation, span) { Delimiter = delimiter; Expression = expression; }
    public string Delimiter { get; }
    public ExpressionSyntax Expression { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; }
}

public sealed class InterpolatedStringExpressionSyntax : ExpressionSyntax
{
    internal InterpolatedStringExpressionSyntax(SourceSpan span, IReadOnlyList<InterpolatedPartSyntax> parts, string raw) : base(SyntaxKind.InterpolatedStringExpression, span) { Parts = parts; Raw = raw; }
    public IReadOnlyList<InterpolatedPartSyntax> Parts { get; }
    public string Raw { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Parts) yield return item; }
}

public sealed class UnaryExpressionSyntax : ExpressionSyntax
{
    internal UnaryExpressionSyntax(SourceSpan span, string @operator, ExpressionSyntax operand, bool postfix = false) : base(SyntaxKind.UnaryExpression, span)
    { Operator = @operator; Operand = operand; IsPostfix = postfix; }
    public string Operator { get; }
    public ExpressionSyntax Operand { get; }
    public bool IsPostfix { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Operand; }
}

public sealed class BinaryExpressionSyntax : ExpressionSyntax
{
    internal BinaryExpressionSyntax(SourceSpan span, string @operator, ExpressionSyntax left, ExpressionSyntax right) : base(SyntaxKind.BinaryExpression, span)
    { Operator = @operator; Left = left; Right = right; }
    public string Operator { get; }
    public ExpressionSyntax Left { get; }
    public ExpressionSyntax Right { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Left; yield return Right; }
}

public sealed class ConditionalExpressionSyntax : ExpressionSyntax
{
    internal ConditionalExpressionSyntax(SourceSpan span, ExpressionSyntax condition, ExpressionSyntax whenTrue, ExpressionSyntax whenFalse) : base(SyntaxKind.ConditionalExpression, span)
    { Condition = condition; WhenTrue = whenTrue; WhenFalse = whenFalse; }
    public ExpressionSyntax Condition { get; }
    public ExpressionSyntax WhenTrue { get; }
    public ExpressionSyntax WhenFalse { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Condition; yield return WhenTrue; yield return WhenFalse; }
}

public sealed class PostfixIfExpressionSyntax : ExpressionSyntax
{
    internal PostfixIfExpressionSyntax(SourceSpan span, ExpressionSyntax expression, ExpressionSyntax condition) : base(SyntaxKind.PostfixIfExpression, span) { Expression = expression; Condition = condition; }
    public ExpressionSyntax Expression { get; }
    public ExpressionSyntax Condition { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; yield return Condition; }
}

public sealed class MemberExpressionSyntax : ExpressionSyntax
{
    internal MemberExpressionSyntax(SourceSpan span, ExpressionSyntax target, string member) : base(SyntaxKind.MemberExpression, span) { Target = target; Member = member; }
    public ExpressionSyntax Target { get; }
    public string Member { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Target; }
}

public sealed class WithMemberExpressionSyntax : ExpressionSyntax
{
    internal WithMemberExpressionSyntax(SourceSpan span, string member) : base(SyntaxKind.WithMemberExpression, span) => Member = member;
    public string Member { get; }
}

public sealed class IndexExpressionSyntax : ExpressionSyntax
{
    internal IndexExpressionSyntax(SourceSpan span, ExpressionSyntax target, ExpressionSyntax index) : base(SyntaxKind.IndexExpression, span) { Target = target; Index = index; }
    public ExpressionSyntax Target { get; }
    public ExpressionSyntax Index { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Target; yield return Index; }
}

public sealed class ArgumentSyntax : SyntaxNode
{
    internal ArgumentSyntax(SourceSpan span, ExpressionSyntax? expression, bool isExpanded, bool isOmitted) : base(SyntaxKind.Argument, span)
    { Expression = expression; IsExpanded = isExpanded; IsOmitted = isOmitted; }
    public ExpressionSyntax? Expression { get; }
    public bool IsExpanded { get; }
    public bool IsOmitted { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { if (Expression != null) yield return Expression; }
}

public sealed class CallExpressionSyntax : ExpressionSyntax
{
    internal CallExpressionSyntax(SourceSpan span, ExpressionSyntax target, IReadOnlyList<ArgumentSyntax> arguments) : base(SyntaxKind.CallExpression, span) { Target = target; Arguments = arguments; }
    public ExpressionSyntax Target { get; }
    public IReadOnlyList<ArgumentSyntax> Arguments { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Target; foreach (var item in Arguments) yield return item; }
}

public sealed class NewExpressionSyntax : ExpressionSyntax
{
    internal NewExpressionSyntax(SourceSpan span, ExpressionSyntax expression) : base(SyntaxKind.NewExpression, span) => Expression = expression;
    public ExpressionSyntax Expression { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Expression; }
}

public sealed class FunctionExpressionSyntax : ExpressionSyntax
{
    internal FunctionExpressionSyntax(SourceSpan span, IReadOnlyList<ParameterSyntax> parameters, string? returnType, BlockStatementSyntax body) : base(SyntaxKind.FunctionExpression, span)
    { Parameters = parameters; ReturnType = returnType; Body = body; }
    public IReadOnlyList<ParameterSyntax> Parameters { get; }
    public string? ReturnType { get; }
    public BlockStatementSyntax Body { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Parameters) yield return item; yield return Body; }
}

public sealed class ArrayElementSyntax : SyntaxNode
{
    internal ArrayElementSyntax(SourceSpan span, ExpressionSyntax? expression) : base(SyntaxKind.ArrayElement, span) => Expression = expression;
    public ExpressionSyntax? Expression { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { if (Expression != null) yield return Expression; }
}

public sealed class ArrayExpressionSyntax : ExpressionSyntax
{
    internal ArrayExpressionSyntax(SourceSpan span, IReadOnlyList<ArrayElementSyntax> elements, bool isConst) : base(isConst ? SyntaxKind.ConstArrayExpression : SyntaxKind.ArrayExpression, span)
    { Elements = elements; IsConst = isConst; }
    public IReadOnlyList<ArrayElementSyntax> Elements { get; }
    public bool IsConst { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Elements) yield return item; }
}

public sealed class DictionaryEntrySyntax : SyntaxNode
{
    internal DictionaryEntrySyntax(SourceSpan span, ExpressionSyntax key, ExpressionSyntax value, string separator) : base(SyntaxKind.DictionaryEntry, span)
    { Key = key; Value = value; Separator = separator; }
    public ExpressionSyntax Key { get; }
    public ExpressionSyntax Value { get; }
    public string Separator { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { yield return Key; yield return Value; }
}

public sealed class DictionaryExpressionSyntax : ExpressionSyntax
{
    internal DictionaryExpressionSyntax(SourceSpan span, IReadOnlyList<DictionaryEntrySyntax> entries, bool isConst) : base(isConst ? SyntaxKind.ConstDictionaryExpression : SyntaxKind.DictionaryExpression, span)
    { Entries = entries; IsConst = isConst; }
    public IReadOnlyList<DictionaryEntrySyntax> Entries { get; }
    public bool IsConst { get; }
    public override IEnumerable<SyntaxNode> ChildNodes() { foreach (var item in Entries) yield return item; }
}

public sealed class ErrorExpressionSyntax : ExpressionSyntax
{
    internal ErrorExpressionSyntax(SourceSpan span, string message) : base(SyntaxKind.ErrorExpression, span) => Message = message;
    public string Message { get; }
}
