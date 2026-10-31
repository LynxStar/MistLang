namespace Mist.Backends.CSharpTranspiler.Razor;

/// <summary>
/// The only Mist lexical knowledge the markup parser holds: where a string, character, regex or
/// interpolated literal or a line comment ends, so that a bracket inside one is never counted. An
/// unclosed literal runs to the end of the text; an unclosed bracket has no close, -1.
/// </summary>
internal static class MistBalancer
{
    public static int SkipLiteral(string text, int index)
    {
        var c = text[index];
        var next = index + 1 < text.Length ? text[index + 1] : '\0';
        if(c == '/' && next == '/')
        {
            var newline = text.IndexOf('\n', index);
            return newline < 0 ? text.Length : newline;
        }

        if(c == '$' && next == '"')
        {
            return SkipInterpolated(text, index + 2);
        }

        if(c == '"')
        {
            return SkipQuoted(text, index + 1, '"');
        }

        if(c == '`')
        {
            return SkipQuoted(text, index + 1, '`');
        }

        return c == '\'' ? SkipCharacter(text, index) : -1;
    }

    public static int FindClose(string text, int open)
    {
        var expected = new Stack<char>();
        var index = open;
        while(index < text.Length)
        {
            var skipped = SkipLiteral(text, index);
            if(skipped >= 0)
            {
                index = skipped;
                continue;
            }

            var c = text[index];
            if(Closer(c) is { } closer)
            {
                expected.Push(closer);
            }
            else if(expected.Count > 0 && c == expected.Peek())
            {
                _ = expected.Pop();
                if(expected.Count == 0)
                {
                    return index;
                }
            }

            index++;
        }

        return -1;
    }

    public static char? Closer(char open)
    {
        return open switch
        {
            '(' => ')',
            '[' => ']',
            '{' => '}',
            _ => null,
        };
    }

    private static int SkipQuoted(string text, int index, char quote)
    {
        while(index < text.Length)
        {
            if(text[index] == '\\')
            {
                index += 2;
                continue;
            }

            if(text[index] == quote)
            {
                return index + 1;
            }

            index++;
        }

        return text.Length;
    }

    private static int SkipInterpolated(string text, int index)
    {
        while(index < text.Length)
        {
            switch(text[index])
            {
                case '\\':
                    index += 2;
                    continue;
                case '"':
                    return index + 1;
                case '{':
                    var close = FindClose(text, index);
                    if(close < 0)
                    {
                        return text.Length;
                    }

                    index = close + 1;
                    continue;
                default:
                    index++;
                    continue;
            }
        }

        return text.Length;
    }

    private static int SkipCharacter(string text, int index)
    {
        var end = index + 1 < text.Length && text[index + 1] == '\\' ? index + 3 : index + 2;
        return end < text.Length && text[end] == '\'' && text[index + 1] != '\'' ? end + 1 : -1;
    }
}