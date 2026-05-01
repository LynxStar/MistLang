using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Maps Mist semantic model types to their C# source-code representations.
/// For v1 the mapping is 1:1, but this class isolates the backend-specific
/// knowledge so the semantic model stays backend-agnostic.
///
/// <para>
/// <b>Migration status (Task 32 of the Mist Standard Library spec):</b>
/// During the migration window the legacy
/// <see cref="PrimitiveType"/> / <see cref="StringType"/> singletons
/// coexist with the stdlib <c>Standard.*</c> <see cref="StructType"/> /
/// <see cref="ClassType"/> instances. This mapper accepts both; Stage 5
/// (Task 39) rewrites it into the pure Backing_Type_Map form keyed on
/// stdlib types only.
/// </para>
/// </summary>
internal static class CSharpTypeMap
{
    /// <summary>
    /// Lexeme-indexed map from primitive keyword to C# source keyword.
    /// Keying by the Mist keyword lexeme (e.g., <c>"int"</c>) avoids
    /// tying the mapping to a specific <see cref="IType"/> instance —
    /// critical during the migration window, when a single primitive can
    /// be represented by either the legacy
    /// <see cref="PrimitiveType"/> singleton or the stdlib
    /// <c>Standard.*</c> <see cref="StructType"/> / <see cref="ClassType"/>
    /// instance.
    /// </summary>
    private static readonly Dictionary<string, string> KeywordByDisplayName = new(StringComparer.Ordinal)
    {
        // Legacy Mist-primitive lexemes.
        ["int"] = "int",
        ["uint"] = "uint",
        ["long"] = "long",
        ["ulong"] = "ulong",
        ["short"] = "short",
        ["ushort"] = "ushort",
        ["byte"] = "byte",
        ["sbyte"] = "sbyte",
        ["float"] = "float",
        ["double"] = "double",
        ["decimal"] = "decimal",
        ["bool"] = "bool",
        ["char"] = "char",
        ["string"] = "string",

        // Stdlib Backend_Backed_Type simple names. Task 31 of the migration
        // flips primitive-keyword resolution to these stdlib types; the
        // emitter still needs to recover the C# source keyword.
        ["Int32"] = "int",
        ["UInt32"] = "uint",
        ["Int64"] = "long",
        ["UInt64"] = "ulong",
        ["Int16"] = "short",
        ["UInt16"] = "ushort",
        ["Byte"] = "byte",
        ["SByte"] = "sbyte",
        ["Single"] = "float",
        ["Double"] = "double",
        ["Decimal"] = "decimal",
        ["Boolean"] = "bool",
        ["Char"] = "char",
        ["String"] = "string",
    };

    /// <summary>
    /// Returns the C# keyword for a Mist primitive type — accepts either
    /// the legacy <see cref="PrimitiveType"/> (<c>DisplayName = "int"</c>)
    /// or a stdlib <c>Standard.Int32</c>-style type
    /// (<c>DisplayName = "Int32"</c>, <c>ContainingNamespace = "Standard"</c>).
    /// </summary>
    public static string GetKeyword(IType type) =>
        KeywordByDisplayName.TryGetValue(type.DisplayName, out var keyword)
            ? keyword
            : throw new ArgumentException($"Unknown primitive type: {type.DisplayName}");

    /// <summary>
    /// Returns the C# source representation of any Mist IType.
    /// When <paramref name="isValueContext"/> is <c>true</c>, void maps to
    /// <c>MistVoid</c> (the unit-type struct) instead of the C# <c>void</c> keyword.
    /// The default <c>false</c> preserves backward compatibility for all existing callers.
    /// </summary>
    public static string GetTypeName(IType type, bool isValueContext = false) => type switch
    {
        // Stdlib Backend_Backed_Types (post Task 31) — routed to a C#
        // source keyword via the Backing_Type_Map. Matched first so any
        // Standard.* type does not fall through to the generic
        // ClassType / StructType branches below (which would emit the
        // Mist simple name — e.g., "Int32" — instead of the C# keyword).
        StructType st when st.IsBackendBacked && st.ContainingNamespace == "Standard"
            => GetKeyword(st),
        ClassType ct when ct.IsBackendBacked && ct.ContainingNamespace == "Standard"
            => GetKeyword(ct),

        // Legacy singleton branches — still reachable from test harnesses
        // and from any code path that resolves primitives before the
        // stdlib is registered into PrimitiveTypeRegistry.
        PrimitiveType p => GetKeyword(p),
        StringType => "string",

        VoidType => isValueContext ? "MistVoid" : "void",
        ArrayType a => $"{GetTypeName(a.ElementType, isValueContext)}[]",
        TypeType => "System.Type",
        TypeParameterType tp => tp.Name,
        ConstructedType ct => $"{GetTypeName(ct.GenericDefinition, isValueContext)}<{string.Join(", ", ct.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",
        ClassType c => c.DisplayName,
        StructType s => s.DisplayName,
        InterfaceType i => i.DisplayName,
        _ => throw new ArgumentException($"Cannot map type to C#: {type.DisplayName}"),
    };
}
