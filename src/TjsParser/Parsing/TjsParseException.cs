using System;

namespace TjsParser.Parsing;

public sealed class TjsParseException : Exception
{
    public TjsParseException(Diagnostic diagnostic) : base(diagnostic.Message) => Diagnostic = diagnostic;
    public Diagnostic Diagnostic { get; }
}
