namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

/// <summary>
/// Translates between Mist arity notation (e.g., "List&lt;&gt;") and CLR backtick
/// notation (e.g., "List`1") for generic type names.
/// </summary>
internal static class NameFormatTranslator
{
    /// <summary>
    /// Converts Mist arity notation to CLR backtick notation.
    /// "List&lt;&gt;" -> "List`1", "Dict&lt;,&gt;" -> "Dict`2".
    /// Non-generic names pass through unchanged.
    /// </summary>
    public static string MistNameToClrName(string mistName)
    {
        var angleBracketStart = mistName.IndexOf('<');
        if(angleBracketStart < 0)
        {
            return mistName;
        }

        var baseName = mistName[..angleBracketStart];
        var commas = mistName.Count(c => c == ',');
        var arity = commas + 1;
        return $"{baseName}`{arity}";
    }

    /// <summary>
    /// Converts CLR backtick notation to Mist arity notation for registration.
    /// "List`1" -> "List&lt;&gt;", "Dictionary`2" -> "Dictionary&lt;,&gt;".
    /// Non-generic names pass through unchanged.
    /// </summary>
    public static string ClrNameToMistName(string clrName)
    {
        var backtickIndex = clrName.IndexOf('`');
        if(backtickIndex < 0)
        {
            return clrName;
        }

        var baseName = clrName[..backtickIndex];
        var arityStr = clrName[(backtickIndex + 1)..];
        if(!int.TryParse(arityStr, out var arity) || arity < 1)
        {
            return clrName;
        }

        var commas = new string(',', arity - 1);
        return $"{baseName}<{commas}>";
    }
}
