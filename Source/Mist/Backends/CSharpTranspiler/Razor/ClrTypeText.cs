using System.Collections.Frozen;

namespace Mist.Backends.CSharpTranspiler.Razor;

internal static class ClrTypeText
{
    private static readonly FrozenDictionary<string, string> Keywords = new Dictionary<string, string>
    {
        ["System.Boolean"] = "bool",
        ["System.Byte"] = "byte",
        ["System.SByte"] = "sbyte",
        ["System.Int16"] = "short",
        ["System.UInt16"] = "ushort",
        ["System.Int32"] = "int",
        ["System.UInt32"] = "uint",
        ["System.Int64"] = "long",
        ["System.UInt64"] = "ulong",
        ["System.Single"] = "float",
        ["System.Double"] = "double",
        ["System.Decimal"] = "decimal",
        ["System.Char"] = "char",
        ["System.String"] = "string",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    public static string Of(Type type)
    {
        if(type.IsArray)
        {
            return Of(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if(type.IsGenericParameter)
        {
            return type.Name;
        }

        var name = Name(type);
        if(!type.IsGenericType)
        {
            return Keywords.GetValueOrDefault(name, name);
        }

        var arguments = type.GetGenericArguments();
        return name == "System.Nullable"
            ? Of(arguments[0]) + "?"
            : name + "<" + string.Join(", ", arguments.Select(Of)) + ">";
    }

    public static string Name(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var metadataName = definition.FullName ?? definition.Name;
        return string.Join('.', metadataName.Split('+').Select(segment => segment.Split('`')[0]));
    }
}