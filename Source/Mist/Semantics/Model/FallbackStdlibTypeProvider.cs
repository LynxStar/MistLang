using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// A fallback <see cref="IStdlibTypeProvider"/> that returns the legacy
/// <see cref="PrimitiveType"/> / <see cref="StringType"/> singletons
/// (or their registry-backed equivalents) when the stdlib has not been
/// loaded. This keeps tests that don't load stdlib working.
/// </summary>
internal sealed class FallbackStdlibTypeProvider : IStdlibTypeProvider
{
    public static readonly FallbackStdlibTypeProvider Instance = new();

    private FallbackStdlibTypeProvider() { }

    public IType LookupStandardType(string simpleName)
    {
        return simpleName switch
        {
            "Int32" => PrimitiveType.Int,
            "UInt32" => PrimitiveType.Uint,
            "Int64" => PrimitiveType.Long,
            "UInt64" => PrimitiveType.Ulong,
            "Int16" => PrimitiveType.Short,
            "UInt16" => PrimitiveType.Ushort,
            "Byte" => PrimitiveType.Byte,
            "SByte" => PrimitiveType.Sbyte,
            "Single" => PrimitiveType.Float,
            "Double" => PrimitiveType.Double,
            "Decimal" => PrimitiveType.Decimal,
            "Boolean" => PrimitiveType.Bool,
            "Char" => PrimitiveType.Char,
            "String" => StringType.Instance,
            _ => throw new InvalidOperationException(
                $"No fallback stdlib type for '{simpleName}'. " +
                "The Standard Library may not be loaded.")
        };
    }
}
