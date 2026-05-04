using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Maps Mist semantic model types to their C# source-code representations.
/// This is the Backing_Type_Map (Requirement 9.7): a backend-private dictionary
/// keyed on fully-qualified stdlib type name that maps each bootstrap type to
/// its C# source keyword. The map is internal to the C# backend and never
/// exposed to the semantic model or analysis layers (Requirement 9.11).
/// </summary>
internal static class CSharpTypeMap
{
    /// <summary>
    /// Backing_Type_Map (Requirement 9.7). Primary keys are fully-qualified
    /// stdlib type names (e.g., <c>"Standard.Int32" → "int"</c>). Legacy
    /// Mist-primitive keyword entries (e.g., <c>"int" → "int"</c>) are
    /// retained for backward compatibility with tests that don't load stdlib.
    /// </summary>
    private static readonly Dictionary<string, string> BackingTypes = new(StringComparer.Ordinal)
    {
        // ── Fully-qualified stdlib type names (Backing_Type_Map form) ─────
        ["Standard.Int32"] = "int",
        ["Standard.UInt32"] = "uint",
        ["Standard.Int64"] = "long",
        ["Standard.UInt64"] = "ulong",
        ["Standard.Int16"] = "short",
        ["Standard.UInt16"] = "ushort",
        ["Standard.Byte"] = "byte",
        ["Standard.SByte"] = "sbyte",
        ["Standard.Single"] = "float",
        ["Standard.Double"] = "double",
        ["Standard.Decimal"] = "decimal",
        ["Standard.Boolean"] = "bool",
        ["Standard.Char"] = "char",
        ["Standard.String"] = "string",
        ["Standard.Void"] = "void",
        ["Standard.Type"] = "System.Type",

        // ── Legacy Mist-primitive keyword lexemes (test compat) ──────────
        // Kept so that tests that build a semantic model without loading
        // stdlib (producing StructType instances with legacy DisplayNames
        // like "int") continue to resolve. Task 50 removes StringType;
        // these entries may be cleaned up at that point.
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
        ["void"] = "void",
        ["Type"] = "System.Type",
    };

    /// <summary>
    /// Returns the C# keyword for a Mist type by looking up its fully-qualified
    /// stdlib name or legacy DisplayName in the Backing_Type_Map.
    /// </summary>
    public static string GetKeyword(IType type)
    {
        // For stdlib Backend_Backed_Types, construct the fully-qualified key.
        var qualifiedKey = type switch
        {
            StructType st when st.IsBackendBacked && st.ContainingNamespace is not null
                => $"{st.ContainingNamespace}.{st.DisplayName}",
            ClassType ct when ct.IsBackendBacked && ct.ContainingNamespace is not null
                => $"{ct.ContainingNamespace}.{ct.DisplayName}",
            _ => null,
        };

        if (qualifiedKey is not null && BackingTypes.TryGetValue(qualifiedKey, out var keyword))
            return keyword;

        // Fallback: legacy DisplayName lookup (tests without stdlib).
        if (BackingTypes.TryGetValue(type.DisplayName, out var legacyKeyword))
            return legacyKeyword;

        throw new ArgumentException($"Unknown primitive type: {type.DisplayName}");
    }

    /// <summary>
    /// Returns the C# source representation of any Mist IType.
    /// When <paramref name="isValueContext"/> is <c>true</c>, void maps to
    /// <c>MistVoid</c> (the unit-type struct) instead of the C# <c>void</c> keyword.
    /// The default <c>false</c> preserves backward compatibility for all existing callers.
    /// </summary>
    public static string GetTypeName(IType type, bool isValueContext = false) => type switch
    {
        // Stdlib Backend_Backed_Types — routed to a C# source keyword via
        // the Backing_Type_Map. Matched first so any Standard.* type does
        // not fall through to the generic ClassType / StructType branches
        // below (which would emit the Mist simple name — e.g., "Int32" —
        // instead of the C# keyword).
        StructType st when st.IsBackendBacked && st.ContainingNamespace == "Standard" && st.IsVoid()
            => isValueContext ? "MistVoid" : "void",
        StructType st when st.IsBackendBacked && st.ContainingNamespace == "Standard"
            => GetKeyword(st),
        ClassType ct when ct.IsBackendBacked && ct.ContainingNamespace == "Standard"
            => GetKeyword(ct),

        // Legacy fallback void (tests without stdlib)
        StructType st when st.IsVoid() => isValueContext ? "MistVoid" : "void",
        // Legacy fallback Type (tests without stdlib)
        ClassType ct when ct.IsTypeType() => "System.Type",

        ArrayType a => $"{GetTypeName(a.ElementType, isValueContext)}[]",
        TupleType t => EmitTupleTypeName(t, isValueContext),
        TypeParameterType tp => tp.Name,
        ConstructedType ct => $"{GetTypeName(ct.GenericDefinition, isValueContext)}<{string.Join(", ", ct.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",
        ClassType c => c.DisplayName,
        StructType s => GetKeywordOrDisplayName(s),
        InterfaceType i => i.DisplayName,
        TraitType t => t.DisplayName,
        _ => throw new ArgumentException($"Cannot map type to C#: {type.DisplayName}"),
    };

    /// <summary>
    /// For a StructType, check if its DisplayName maps to a C# keyword
    /// (handles legacy fallback StructTypes with names like "int", "bool").
    /// </summary>
    private static string GetKeywordOrDisplayName(StructType s)
    {
        if (BackingTypes.TryGetValue(s.DisplayName, out var keyword))
            return keyword;
        return s.DisplayName;
    }

    /// <summary>
    /// Emits a C# tuple type name: <c>(int, string)</c> for unnamed elements,
    /// <c>(int x, string y)</c> for named elements. Each element type is
    /// recursively mapped through <see cref="GetTypeName"/>.
    /// </summary>
    private static string EmitTupleTypeName(TupleType tuple, bool isValueContext)
    {
        var parts = new string[tuple.Arity];
        for (var i = 0; i < tuple.Arity; i++)
        {
            var elementTypeName = GetTypeName(tuple.ElementTypes[i], isValueContext);
            var name = tuple.ElementNames is not null && i < tuple.ElementNames.Count
                ? tuple.ElementNames[i]
                : null;
            parts[i] = name is not null
                ? $"{elementTypeName} {name}"
                : elementTypeName;
        }
        return $"({string.Join(", ", parts)})";
    }
}
