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
    /// Attempts to find a C# mapped name for a Mist type name used in source.
    /// Checks both the simple name (e.g., "Type") and the fully-qualified
    /// stdlib form (e.g., "Standard.Type"). Returns null if no mapping exists
    /// or if the mapped name equals the input (no remapping needed).
    /// </summary>
    public static string? TryGetMappedName(string mistTypeName)
    {
        // Check the simple name first (e.g., "Type" → "System.Type").
        if (BackingTypes.TryGetValue(mistTypeName, out var mapped))
        {
            // Only return if the mapped name differs from the input
            // (skip identity mappings like "int" → "int").
            if (mapped != mistTypeName)
                return mapped;
        }

        // Check with "Standard." prefix (e.g., "Type" → "Standard.Type" → "System.Type").
        if (BackingTypes.TryGetValue($"Standard.{mistTypeName}", out var qualifiedMapped))
        {
            if (qualifiedMapped != mistTypeName)
                return qualifiedMapped;
        }

        return null;
    }

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
    /// <remarks>
    /// <para>
    /// Closed_Generic_With_Unknown values (e.g., Mist <c>List&lt;unknown&gt;</c>,
    /// <c>Map&lt;string, unknown&gt;</c>) lower to C# <c>dynamic</c> rather than
    /// <c>List&lt;object&gt;</c> / <c>Map&lt;string, object&gt;</c>. C# generic
    /// types are invariant at the CLR level — there is no IL coercion from
    /// <c>List&lt;int&gt;</c> to <c>List&lt;object&gt;</c> for user-defined
    /// generics, so the original design's "(List&lt;object&gt;)(object)list"
    /// double-cast throws <see cref="InvalidCastException"/> at runtime.
    /// Emitting as <c>dynamic</c> routes every member access on such a
    /// receiver through the DLR, which dispatches against the receiver's
    /// actual runtime type. Mist's Reachability Analyzer guarantees at
    /// compile time that no write-path member is called on a
    /// Closed_Generic_With_Unknown receiver, so the DLR only sees
    /// reachability-safe calls at runtime.
    /// </para>
    /// <para>
    /// Bare <see cref="UnknownType"/> still lowers to <c>object</c> (not
    /// <c>dynamic</c>) because the type checker rejects member access and
    /// operator dispatch on a bare <c>unknown</c> receiver at compile time
    /// (MIST_TC_0065 / MIST_TC_0066); the receiver never participates in a
    /// dispatch that needs DLR support.
    /// </para>
    /// </remarks>
    public static string GetTypeName(IType type, bool isValueContext = false)
    {
        // Defensive null check: type arguments in ConstructedTraitType or
        // ConstructedType may be null if resolution failed upstream.
        if (type is null)
            return "object";

        return type switch
        {
        // Defensive: type aliases are resolved transparently during type checking,
        // so a TypeAliasType should never reach the type map. If one does leak
        // through, resolve to the underlying type and delegate to existing logic.
        TypeAliasType alias => GetTypeName(alias.UnderlyingType, isValueContext),

        // Enum types map to their record class name (the enum's DisplayName).
        EnumType et => et.DisplayName,

        // Union types lower to the generated helper struct name.
        UnionType ut => UnionHelperGenerator.GetHelperTypeName(ut),

        // User-facing UnknownType (Requirement 15.1): every direct reference to
        // UnknownType lowers to C# `object`. Matched before the generic class/
        // struct branches because UnknownType is a singleton with its own class
        // — it is neither a StructType nor a ClassType and has no
        // Backing_Type_Map entry.
        UnknownType => "object",

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

        // Closed_Generic_With_Unknown (Task 31 revised) — a constructed type
        // whose type arguments transitively contain UnknownType lowers to C#
        // `dynamic`. See the XML remarks above for the rationale.
        ConstructedType ctUnknown when ConstructedTypeContainsUnknown(ctUnknown)
            => "dynamic",

        ConstructedType ct => $"{GetTypeName(ct.GenericDefinition, isValueContext)}<{string.Join(", ", ct.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",
        ClassType c => c.DisplayName,
        StructType s => GetKeywordOrDisplayName(s),
        InterfaceType i => i.DisplayName,
        ConstructedTraitType ctt => $"{ctt.GenericTraitDefinition.DisplayName}<{string.Join(", ", ctt.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",
        TraitType t => t.DisplayName,
        _ => throw new ArgumentException($"Cannot map type to C#: {type.DisplayName}"),
        };
    }

    /// <summary>
    /// Structural test: returns <c>true</c> when <paramref name="ct"/> is a
    /// Closed_Generic_With_Unknown — a constructed type whose type arguments
    /// transitively contain <see cref="UnknownType"/> at any depth. Used by
    /// <see cref="GetTypeName"/> to decide whether to lower the type to C#
    /// <c>dynamic</c> instead of the naive <c>Foo&lt;object&gt;</c> form.
    /// </summary>
    private static bool ConstructedTypeContainsUnknown(ConstructedType ct)
    {
        foreach (var arg in ct.TypeArguments)
        {
            if (TypeContainsUnknown(arg))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursive helper for <see cref="ConstructedTypeContainsUnknown"/>.
    /// Returns <c>true</c> if <paramref name="type"/> is
    /// <see cref="UnknownType"/>, or an array whose element type contains
    /// unknown, or a constructed type whose arguments contain unknown.
    /// </summary>
    private static bool TypeContainsUnknown(IType type)
    {
        return type switch
        {
            UnknownType => true,
            ArrayType a => TypeContainsUnknown(a.ElementType),
            ConstructedType ct => ConstructedTypeContainsUnknown(ct),
            _ => false,
        };
    }

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
