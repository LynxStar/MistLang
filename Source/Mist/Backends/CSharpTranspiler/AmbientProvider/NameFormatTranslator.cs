namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class NameFormatTranslator
{
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