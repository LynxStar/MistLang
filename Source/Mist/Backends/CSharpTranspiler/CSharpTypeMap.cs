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
        ["Standard.Regex"] = "System.Text.RegularExpressions.Regex",
        ["Standard.Void"] = "void",
        ["Standard.Type"] = "System.Type",
        ["Standard.Console"] = "System.Console",
        ["Standard.Math"] = "System.Math",
        ["Standard.Convert"] = "System.Convert",
        ["Standard.Task"] = "System.Threading.Tasks.Task",
        ["Standard.IEnumerator"] = "System.Collections.Generic.IEnumerator",
        ["Standard.IEnumerable"] = "System.Collections.Generic.IEnumerable",

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
        ["regex"] = "System.Text.RegularExpressions.Regex",
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
    /// actual runtime type. Mist's ReadonlyAnalyzer guarantees at
    /// compile time that no non-readonly member is called on a
    /// Closed_Generic_With_Unknown receiver, so the DLR only sees
    /// readonly-safe calls at runtime.
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

        // Refinement types are erased to their base type in C#. For example,
        // `AdultAge` (defined as `type AdultAge = int >= 18;`) emits as `int`.
        RefinementType refinement => GetTypeName(refinement.BaseType, isValueContext),

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

        // Standard.Task<T> — maps to System.Threading.Tasks.Task<T> or non-generic Task.
        // Task<Void> where value is not used → "Task" (non-generic).
        // Task<Void> where value is used (isValueContext) → "Task<MistVoid>".
        // Task<T> where T is not Void → "Task<T>".
        // For the initial implementation, always emit Task<T> (ValueTask optimization comes later).
        ConstructedType ctTask when IsStandardTask(ctTask)
            => EmitTaskTypeName(ctTask, isValueContext),

        // Closed_Generic_With_Unknown (Task 31 revised) — a constructed type
        // whose type arguments transitively contain UnknownType lowers to C#
        // `dynamic`. See the XML remarks above for the rationale.
        ConstructedType ctUnknown when ConstructedTypeContainsUnknown(ctUnknown)
            => "dynamic",

        ConstructedType ct when ct.DependentArguments is not null
            => EmitConstructedTypeWithDependentArgs(ct, isValueContext),

        ConstructedType ct => $"{GetTypeName(ct.GenericDefinition, isValueContext)}<{string.Join(", ", ct.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",
        NewtypeType nt => nt.Name,
        ClassType c => c.DisplayName,
        StructType s => GetKeywordOrDisplayName(s),
        InterfaceType i when i.ContainingNamespace == "Standard" && TryGetMappedName($"Standard.{i.DisplayName}") is { } mapped => mapped,
        InterfaceType i => i.DisplayName,
        ConstructedTraitType ctt => $"{ctt.GenericTraitDefinition.DisplayName}<{string.Join(", ", ctt.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",
        TraitType t => t.DisplayName,
        _ => throw new ArgumentException($"Cannot map type to C#: {type.DisplayName}"),
        };
    }

    /// <summary>
    /// Emits a <see cref="ConstructedType"/> that has dependent value arguments.
    /// For positions with a concrete dependent value, emits the carrier struct name.
    /// For positions with a forwarded (open) dependent parameter, emits the T-prefixed parameter name.
    /// For regular type argument positions, emits the standard type name.
    /// </summary>
    private static string EmitConstructedTypeWithDependentArgs(ConstructedType ct, bool isValueContext)
    {
        var argParts = new string[ct.TypeArguments.Count];
        for (var i = 0; i < ct.TypeArguments.Count; i++)
        {
            if (ct.DependentArguments is not null && i < ct.DependentArguments.Count && ct.DependentArguments[i] is { } depArg)
            {
                // Concrete dependent value → carrier struct name
                var structName = CarrierStructRegistry.GetStructName(depArg);
                if (structName is not null)
                {
                    argParts[i] = structName;
                }
                else
                {
                    // Open/forwarded parameter — the Value is a string (the parameter name)
                    argParts[i] = depArg.Value is string paramName ? $"T{paramName}" : GetTypeName(ct.TypeArguments[i], isValueContext);
                }
            }
            else
            {
                argParts[i] = GetTypeName(ct.TypeArguments[i], isValueContext);
            }
        }
        return $"{GetTypeName(ct.GenericDefinition, isValueContext)}<{string.Join(", ", argParts)}>";
    }

    /// <summary>
    /// Returns <c>true</c> when the <see cref="ConstructedType"/>'s generic definition
    /// is the <c>Standard.Task</c> class (the Mist concurrency primitive).
    /// </summary>
    private static bool IsStandardTask(ConstructedType ct)
    {
        return ct.GenericDefinition is ClassType { DisplayName: "Task", ContainingNamespace: "Standard", IsBackendBacked: true };
    }

    /// <summary>
    /// Emits the C# type name for a <c>Standard.Task&lt;T&gt;</c> constructed type.
    /// <list type="bullet">
    ///   <item><c>Task&lt;Void&gt;</c> (value not used) → <c>Task</c> (non-generic)</item>
    ///   <item><c>Task&lt;Void&gt;</c> (value used / isValueContext) → <c>Task&lt;MistVoid&gt;</c></item>
    ///   <item><c>Task&lt;T&gt;</c> where T is not Void → <c>Task&lt;{GetTypeName(T)}&gt;</c></item>
    /// </list>
    /// For the initial implementation, always emits <c>Task&lt;T&gt;</c> (the ValueTask
    /// optimization is deferred to task 12.6).
    /// </summary>
    private static string EmitTaskTypeName(ConstructedType ct, bool isValueContext)
    {
        var innerType = ct.TypeArguments[0];

        if (innerType.IsVoid())
        {
            // Task<Void> where value is used → Task<MistVoid>
            if (isValueContext)
                return "System.Threading.Tasks.Task<MistVoid>";

            // Task<Void> where value is not used → non-generic Task
            return "System.Threading.Tasks.Task";
        }

        // Task<T> where T is not Void → Task<T>
        var innerTypeName = GetTypeName(innerType, isValueContext: true);
        return $"System.Threading.Tasks.Task<{innerTypeName}>";
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
