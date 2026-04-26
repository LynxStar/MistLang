using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Maps Mist semantic model types to their C# source-code representations.
/// For v1 the mapping is 1:1, but this class isolates the backend-specific
/// knowledge so the semantic model stays backend-agnostic.
/// </summary>
internal static class CSharpTypeMap
{
    private static readonly Dictionary<PrimitiveType, string> PrimitiveKeywords = new()
    {
        [PrimitiveType.Int] = "int",
        [PrimitiveType.Uint] = "uint",
        [PrimitiveType.Long] = "long",
        [PrimitiveType.Ulong] = "ulong",
        [PrimitiveType.Short] = "short",
        [PrimitiveType.Ushort] = "ushort",
        [PrimitiveType.Byte] = "byte",
        [PrimitiveType.Sbyte] = "sbyte",
        [PrimitiveType.Float] = "float",
        [PrimitiveType.Double] = "double",
        [PrimitiveType.Decimal] = "decimal",
        [PrimitiveType.Bool] = "bool",
        [PrimitiveType.Char] = "char",
    };

    /// <summary>
    /// Returns the C# keyword for a Mist primitive type.
    /// </summary>
    public static string GetKeyword(PrimitiveType type) =>
        PrimitiveKeywords.TryGetValue(type, out var keyword)
            ? keyword
            : throw new ArgumentException($"Unknown primitive type: {type.DisplayName}");

    /// <summary>
    /// Returns the C# source representation of any Mist IType.
    /// </summary>
    public static string GetTypeName(IType type) => type switch
    {
        PrimitiveType p => GetKeyword(p),
        StringType => "string",
        VoidType => "void",
        ArrayType a => $"{GetTypeName(a.ElementType)}[]",
        ClassType c => c.DisplayName,
        StructType s => s.DisplayName,
        _ => throw new ArgumentException($"Cannot map type to C#: {type.DisplayName}"),
    };
}
