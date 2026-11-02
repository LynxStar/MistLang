using System.Text;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal static class MistLiteral
{
    public static string String(string value)
    {
        var literal = new StringBuilder("\"", value.Length + 2);
        foreach(var c in value)
        {
            _ = c switch
            {
                '\\' => literal.Append("\\\\"),
                '"' => literal.Append("\\\""),
                '\n' => literal.Append("\\n"),
                '\r' => literal.Append("\\r"),
                '\t' => literal.Append("\\t"),
                '\0' => literal.Append("\\0"),
                _ => literal.Append(c),
            };
        }

        return literal.Append('"').ToString();
    }
}