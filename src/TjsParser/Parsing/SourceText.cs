using System;
using System.Collections.Generic;
using TjsParser.Syntax;

namespace TjsParser.Parsing;

internal sealed class SourceText
{
    private readonly int[] _lineStarts;

    public SourceText(string text)
    {
        Text = text ?? throw new ArgumentNullException(nameof(text));
        var starts = new List<int> { 0 };
        for (var i = 0; i < text.Length; i++) if (text[i] == '\n') starts.Add(i + 1);
        _lineStarts = starts.ToArray();
    }

    public string Text { get; }
    public int Length => Text.Length;

    public SourcePosition Position(int offset)
    {
        offset = Math.Max(0, Math.Min(offset, Text.Length));
        var index = Array.BinarySearch(_lineStarts, offset);
        if (index < 0) index = ~index - 1;
        return new SourcePosition(offset, index + 1, offset - _lineStarts[index] + 1);
    }

    public SourceSpan Span(int start, int end) => new SourceSpan(Position(start), Position(end));
}
