using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// A fallback <see cref="IStdlibTypeProvider"/> that returns lightweight
/// <see cref="StructType"/> / <see cref="ClassType"/> instances with the
/// legacy display names when the stdlib has not been loaded. This keeps
/// tests that don't load stdlib working.
///
/// <para>
/// <b>Post-Task 50:</b> <c>PrimitiveType</c> and <c>StringType</c> have
/// been deleted. The fallback now creates its own <see cref="StructType"/>
/// and <see cref="ClassType"/> instances with the legacy keyword display
/// names (e.g., <c>"int"</c>, <c>"bool"</c>, <c>"string"</c>).
/// </para>
/// </summary>
internal sealed class FallbackStdlibTypeProvider : IStdlibTypeProvider
{
    public static readonly FallbackStdlibTypeProvider Instance = new();

    // Lightweight fallback singletons with legacy keyword display names.
    // These use empty scopes since they are only used as type identity
    // markers in tests that don't load the full stdlib.
    private static readonly StructType FallbackInt = new("int", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackUint = new("uint", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackLong = new("long", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackUlong = new("ulong", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackShort = new("short", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackUshort = new("ushort", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackByte = new("byte", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackSbyte = new("sbyte", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackFloat = new("float", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackDouble = new("double", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackDecimal = new("decimal", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackBool = new("bool", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackChar = new("char", new Scope(null), containingNamespace: null);
    private static readonly ClassType FallbackString = new("string", new Scope(null), containingNamespace: null);
    private static readonly StructType FallbackVoid = new("void", new Scope(null), containingNamespace: null);
    private static readonly ClassType FallbackType = new("Type", new Scope(null), containingNamespace: null);

    private FallbackStdlibTypeProvider() { }

    public IType LookupStandardType(string simpleName)
    {
        return simpleName switch
        {
            "Int32" => FallbackInt,
            "UInt32" => FallbackUint,
            "Int64" => FallbackLong,
            "UInt64" => FallbackUlong,
            "Int16" => FallbackShort,
            "UInt16" => FallbackUshort,
            "Byte" => FallbackByte,
            "SByte" => FallbackSbyte,
            "Single" => FallbackFloat,
            "Double" => FallbackDouble,
            "Decimal" => FallbackDecimal,
            "Boolean" => FallbackBool,
            "Char" => FallbackChar,
            "String" => FallbackString,
            "Void" => FallbackVoid,
            "Type" => FallbackType,
            _ => throw new InvalidOperationException(
                $"No fallback stdlib type for '{simpleName}'. " +
                "The Standard Library may not be loaded.")
        };
    }
}
