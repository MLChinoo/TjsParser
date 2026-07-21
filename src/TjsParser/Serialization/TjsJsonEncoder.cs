using System;
using System.Text.Encodings.Web;

namespace TjsParser.Serialization;

/// <summary>
/// A JSON encoder that preserves every valid Unicode scalar value and escapes
/// only characters whose escaping is required by the JSON grammar.
/// </summary>
public sealed class TjsJsonEncoder : JavaScriptEncoder
{
    public static JavaScriptEncoder Instance { get; } = new TjsJsonEncoder();

    private TjsJsonEncoder()
    {
    }

    public override int MaxOutputCharactersPerInputCharacter => 6;

    public override unsafe int FindFirstCharacterToEncode(char* text, int textLength)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));

        for (var index = 0; index < textLength; index++)
        {
            var value = text[index];
            if (NeedsEscaping(value)) return index;

            if (char.IsHighSurrogate(value))
            {
                if (index + 1 >= textLength || !char.IsLowSurrogate(text[index + 1])) return index;
                index++;
            }
            else if (char.IsLowSurrogate(value))
            {
                return index;
            }
        }

        return -1;
    }

    public override bool WillEncode(int unicodeScalar)
    {
        if (unicodeScalar < 0 || unicodeScalar > 0x10FFFF) return true;
        if (unicodeScalar >= 0xD800 && unicodeScalar <= 0xDFFF) return true;
        return unicodeScalar <= char.MaxValue && NeedsEscaping((char)unicodeScalar);
    }

    public override unsafe bool TryEncodeUnicodeScalar(
        int unicodeScalar,
        char* buffer,
        int bufferLength,
        out int numberOfCharactersWritten)
    {
        numberOfCharactersWritten = 0;
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (unicodeScalar < 0 || unicodeScalar > 0x10FFFF ||
            (unicodeScalar >= 0xD800 && unicodeScalar <= 0xDFFF))
        {
            return false;
        }

        if (WillEncode(unicodeScalar))
        {
            var escaped = ShortEscape((char)unicodeScalar);
            if (escaped != '\0')
            {
                if (bufferLength < 2) return false;
                buffer[0] = '\\';
                buffer[1] = escaped;
                numberOfCharactersWritten = 2;
                return true;
            }

            if (bufferLength < 6) return false;
            buffer[0] = '\\';
            buffer[1] = 'u';
            buffer[2] = '0';
            buffer[3] = '0';
            buffer[4] = Hex((unicodeScalar >> 4) & 0xF);
            buffer[5] = Hex(unicodeScalar & 0xF);
            numberOfCharactersWritten = 6;
            return true;
        }

        if (unicodeScalar <= char.MaxValue)
        {
            if (bufferLength < 1) return false;
            buffer[0] = (char)unicodeScalar;
            numberOfCharactersWritten = 1;
            return true;
        }

        if (bufferLength < 2) return false;
        var surrogateValue = unicodeScalar - 0x10000;
        buffer[0] = (char)(0xD800 + (surrogateValue >> 10));
        buffer[1] = (char)(0xDC00 + (surrogateValue & 0x3FF));
        numberOfCharactersWritten = 2;
        return true;
    }

    private static bool NeedsEscaping(char value) => value <= '\u001F' || value == '"' || value == '\\';

    private static char ShortEscape(char value)
    {
        switch (value)
        {
            case '"': return '"';
            case '\\': return '\\';
            case '\b': return 'b';
            case '\t': return 't';
            case '\n': return 'n';
            case '\f': return 'f';
            case '\r': return 'r';
            default: return '\0';
        }
    }

    private static char Hex(int value) => (char)(value < 10 ? '0' + value : 'A' + value - 10);
}
