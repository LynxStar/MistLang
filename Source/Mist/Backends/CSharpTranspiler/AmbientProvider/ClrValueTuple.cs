namespace Mist.Backends.CSharpTranspiler.AmbientProvider;

internal static class ClrValueTuple
{
    public const int RestPosition = 7;

    public static int? Cardinality(Type type)
    {
        if(!type.IsGenericType
            || type.IsGenericTypeDefinition
            || type.Namespace != "System"
            || !type.Name.StartsWith("ValueTuple`", StringComparison.Ordinal))
        {
            return null;
        }

        var arguments = type.GetGenericArguments();

        if(arguments.Length <= RestPosition)
        {
            return arguments.Length;
        }

        return Cardinality(arguments[RestPosition]) is { } rest ? RestPosition + rest : null;
    }

    /// <summary>
    /// How many names a <c>TupleElementNamesAttribute</c> on a signature of this type holds. The C#
    /// compiler writes them in pre-order over the CLR type: each tuple takes one per element it
    /// flattens to, then its arguments are visited in turn -- the Rest among them, which takes its own
    /// names again. Octet's eight elements therefore carry nine names, the last always null.
    /// </summary>
    public static int NameSlots(Type type)
    {
        if(type.HasElementType)
        {
            return NameSlots(type.GetElementType()!);
        }

        if(!type.IsGenericType)
        {
            return 0;
        }

        return (Cardinality(type) ?? 0) + type.GetGenericArguments().Sum(NameSlots);
    }
}