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
    internal static readonly Dictionary<string, string> BackingTypes = new(StringComparer.Ordinal)
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
        ["Standard.ValueTask"] = "System.Threading.Tasks.ValueTask",
        ["Standard.IEnumerator"] = "System.Collections.Generic.IEnumerator",
        ["Standard.IEnumerable"] = "System.Collections.Generic.IEnumerable",
        ["Standard.Span"] = "System.Span",
        ["Standard.ReadOnlySpan"] = "System.ReadOnlySpan",

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

        // Two-constituent void unions (T | void) lower to C# Nullable<T> for value types
        // or plain T for reference types (Requirement 7.1, 7.8).
        UnionType ut when TryGetOptionalTypeName(ut, isValueContext) is { } optName => optName,

        // Multi-constituent unions (or non-void unions) lower to the generated helper struct name.
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
            && ct.DisplayName != "Func" && ct.DisplayName != "Task" && ct.DisplayName != "ValueTask"
            => GetKeyword(ct),
        // Bare Func/Task/ValueTask class types (generic definitions without type arguments)
        // should not normally reach the type map. Emit as 'object' as a safe fallback.
        ClassType ct when ct.IsBackendBacked && ct.ContainingNamespace == "Standard"
            && (ct.DisplayName == "Func" || ct.DisplayName == "Task" || ct.DisplayName == "ValueTask")
            => "object",

        // Legacy fallback void (tests without stdlib)
        StructType st when st.IsVoid() => isValueContext ? "MistVoid" : "void",
        // Legacy fallback Type (tests without stdlib)
        ClassType ct when ct.IsTypeType() => "System.Type",

        ArrayType a => $"{GetTypeName(a.ElementType, isValueContext)}[]",

        // SpanType — the Array length-erasure path (Requirements 6.2, 6.3, 7.1, 7.2).
        // Span<T, Length> lowers to System.Span<T> and ReadOnlySpan<T, Length> lowers to
        // System.ReadOnlySpan<T>, erasing the Length type argument — exactly as
        // Array<T, Length> erases to T[] above. This is deliberately NOT the
        // Task/IntrinsicTypeMap single-BackingTypes-entry ConstructedType path.
        SpanType s when s.IsReadOnly => $"System.ReadOnlySpan<{GetTypeName(s.ElementType, isValueContext)}>",
        SpanType s => $"System.Span<{GetTypeName(s.ElementType, isValueContext)}>",

        TupleType t => EmitTupleTypeName(t, isValueContext),
        TypeParameterType tp => tp.Name,

        // Standard.Task<T> — maps to System.Threading.Tasks.Task<T> or non-generic Task.
        // Task<Void> always emits as non-generic "Task".
        // Task<T> where T is not Void → "Task<T>".
        // For the initial implementation, always emit Task<T> (ValueTask optimization comes later).
        ConstructedType ctTask when IsStandardTask(ctTask)
            => EmitTaskTypeName(ctTask, isValueContext),

        // Standard.ValueTask<T> — maps to System.Threading.Tasks.ValueTask<T> or non-generic ValueTask.
        // ValueTask<Void> always emits as non-generic "ValueTask".
        // ValueTask<T> where T is not Void → "ValueTask<T>".
        ConstructedType ctVTask when IsStandardValueTask(ctVTask)
            => EmitValueTaskTypeName(ctVTask, isValueContext),

        // Standard.Func<...> — maps to System.Func<...> / System.Action<...> / System.Action
        // based on whether the last type argument (the return type) is Void.
        // Func<void> → System.Action; Func<T1, void> → System.Action<T1>;
        // Func<T1, ..., TN> (TResult ≠ void) → System.Func<T1, ..., TN>.
        ConstructedType ctFunc when IsStandardFunc(ctFunc)
            => EmitFuncTypeName(ctFunc),

        // Closed_Generic_With_Unknown (Task 31 revised) — a constructed type
        // whose type arguments transitively contain UnknownType lowers to C#
        // `dynamic`. See the XML remarks above for the rationale.
        ConstructedType ctUnknown when ConstructedTypeContainsUnknown(ctUnknown)
            => "dynamic",

        ConstructedType ct when ct.DependentArguments is not null
            => EmitConstructedTypeWithDependentArgs(ct, isValueContext),

        ConstructedType ct => $"{GetTypeName(ct.GenericDefinition, isValueContext)}<{string.Join(", ", ct.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",

        // DelegateType → emit the fully qualified CLR delegate name
        DelegateType dt when dt.TypeArguments.Count > 0
            => $"{dt.ClrNamespace}.{dt.ClrSimpleName}<{string.Join(", ", dt.TypeArguments.Select(t => GetTypeName(t, isValueContext)))}>",
        DelegateType dt
            => $"{dt.ClrNamespace}.{dt.ClrSimpleName}",

        // BaseDelegateType → System.Delegate
        BaseDelegateType => "System.Delegate",

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
    /// is a <c>Standard.Func</c> class (the Mist first-class-function type family).
    /// </summary>
    private static bool IsStandardFunc(ConstructedType ct)
    {
        return ct.GenericDefinition is ClassType { DisplayName: "Func", ContainingNamespace: "Standard", IsBackendBacked: true };
    }

    /// <summary>
    /// Emits the C# type name for a <c>Standard.Func&lt;...&gt;</c> constructed type.
    /// The last type argument is always the return type; preceding arguments are the
    /// parameter types in order.
    /// <list type="bullet">
    ///   <item>Return type <c>void</c>, arity 1 (just the return type) → <c>System.Action</c> (non-generic)</item>
    ///   <item>Return type <c>void</c>, arity N &gt; 1 → <c>System.Action&lt;T1, ..., TN-1&gt;</c></item>
    ///   <item>Return type non-<c>void</c> → <c>System.Func&lt;T1, ..., TN&gt;</c></item>
    /// </list>
    /// Func type arguments are emitted in value context: delegate type arguments are
    /// CLR generic arguments, which cannot be the <c>void</c> keyword (a Func whose last
    /// argument is <c>void</c> is emitted as <c>Action</c>, removing that argument entirely).
    /// </summary>
    private static string EmitFuncTypeName(ConstructedType ct)
    {
        var typeArgs = ct.TypeArguments;
        var returnType = typeArgs[typeArgs.Count - 1];

        if (returnType.IsVoid())
        {
            // Action family: drop the trailing void return type.
            if (typeArgs.Count == 1)
                return "System.Action";

            var actionArgs = new string[typeArgs.Count - 1];
            for (var i = 0; i < actionArgs.Length; i++)
                actionArgs[i] = GetTypeName(typeArgs[i], isValueContext: true);
            return $"System.Action<{string.Join(", ", actionArgs)}>";
        }

        // Func family: all type arguments, including the return type.
        var funcArgs = new string[typeArgs.Count];
        for (var i = 0; i < funcArgs.Length; i++)
            funcArgs[i] = GetTypeName(typeArgs[i], isValueContext: true);
        return $"System.Func<{string.Join(", ", funcArgs)}>";
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
    ///   <item><c>Task&lt;Void&gt;</c> → <c>System.Threading.Tasks.Task</c> (non-generic)</item>
    ///   <item><c>Task&lt;T&gt;</c> where T is not Void → <c>System.Threading.Tasks.Task&lt;{GetTypeName(T)}&gt;</c></item>
    /// </list>
    /// <c>Task&lt;Void&gt;</c> always emits as the non-generic <c>Task</c> because C# uses
    /// <c>Task</c> (not <c>Task&lt;void&gt;</c>) for async operations with no return value.
    /// This is valid even in generic type argument positions (e.g., <c>Func&lt;int, Task&gt;</c>).
    /// For the initial implementation, always emits <c>Task&lt;T&gt;</c> (the ValueTask
    /// optimization is deferred to task 12.6).
    /// </summary>
    private static string EmitTaskTypeName(ConstructedType ct, bool isValueContext)
    {
        var innerType = ct.TypeArguments[0];

        if (innerType.IsVoid())
        {
            // Task<Void> always emits as non-generic Task.
            return "System.Threading.Tasks.Task";
        }

        // Task<T> where T is not Void → Task<T>
        var innerTypeName = GetTypeName(innerType, isValueContext: true);
        return $"System.Threading.Tasks.Task<{innerTypeName}>";
    }

    /// <summary>
    /// Returns <c>true</c> when the <see cref="ConstructedType"/>'s generic definition
    /// is the <c>Standard.ValueTask</c> class (the Mist value-type concurrency primitive).
    /// </summary>
    private static bool IsStandardValueTask(ConstructedType ct)
    {
        return ct.GenericDefinition is ClassType { DisplayName: "ValueTask", ContainingNamespace: "Standard", IsBackendBacked: true };
    }

    /// <summary>
    /// Emits the C# type name for a <c>Standard.ValueTask&lt;T&gt;</c> constructed type.
    /// <list type="bullet">
    ///   <item><c>ValueTask&lt;Void&gt;</c> → <c>System.Threading.Tasks.ValueTask</c> (non-generic)</item>
    ///   <item><c>ValueTask&lt;T&gt;</c> where T is not Void → <c>System.Threading.Tasks.ValueTask&lt;{GetTypeName(T)}&gt;</c></item>
    /// </list>
    /// <c>ValueTask&lt;Void&gt;</c> always emits as the non-generic <c>ValueTask</c> because C# uses
    /// <c>ValueTask</c> (not <c>ValueTask&lt;void&gt;</c>) for async operations with no return value.
    /// </summary>
    private static string EmitValueTaskTypeName(ConstructedType ct, bool isValueContext)
    {
        var innerType = ct.TypeArguments[0];

        if (innerType.IsVoid())
        {
            // ValueTask<Void> always emits as non-generic ValueTask.
            return "System.Threading.Tasks.ValueTask";
        }

        // ValueTask<T> where T is not Void → ValueTask<T>
        var innerTypeName = GetTypeName(innerType, isValueContext: true);
        return $"System.Threading.Tasks.ValueTask<{innerTypeName}>";
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
            // The erased Length is irrelevant to emission; only an unknown *element*
            // type matters, handled exactly like arrays above.
            SpanType s => TypeContainsUnknown(s.ElementType),
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

    /// <summary>
    /// Determines whether a <see cref="UnionType"/> is a two-constituent union where
    /// one constituent is void (i.e., the <c>T | void</c> / <c>T?</c> pattern).
    /// These unions are emitted as C# <c>Nullable&lt;T&gt;</c> for value types or
    /// plain <c>T</c> for reference types, rather than generating a union helper struct.
    /// </summary>
    public static bool IsOptionalUnion(UnionType unionType)
    {
        return unionType.Constituents.Count == 2
            && unionType.Constituents.Any(c => c.IsVoid());
    }

    /// <summary>
    /// For a two-constituent void union (<c>T | void</c>), returns the non-void
    /// constituent type. Returns <c>null</c> if the union is not an optional union.
    /// </summary>
    public static IType? GetOptionalInnerType(UnionType unionType)
    {
        if (!IsOptionalUnion(unionType))
            return null;
        return unionType.Constituents.First(c => !c.IsVoid());
    }

    /// <summary>
    /// Attempts to emit a C# type name for a two-constituent void union (<c>T | void</c>).
    /// Returns <c>null</c> if the union is not an optional union (i.e., has more than two
    /// constituents or does not contain void), in which case the caller should fall through
    /// to the standard union helper struct emission.
    /// <para>
    /// For value types (struct, enum): emits <c>T?</c> (C# <c>Nullable&lt;T&gt;</c>).
    /// For reference types (class): emits plain <c>T</c> (C# null represents absence).
    /// </para>
    /// </summary>
    public static string? TryGetOptionalTypeName(UnionType unionType, bool isValueContext = false)
    {
        if (!IsOptionalUnion(unionType))
            return null;

        var innerType = GetOptionalInnerType(unionType)!;
        var innerTypeName = GetTypeName(innerType, isValueContext);

        // Value types (struct, enum) → Nullable<T> (emitted as T?)
        if (IsValueType(innerType))
            return $"{innerTypeName}?";

        // Reference types (class) → plain T (null represents void)
        return innerTypeName;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type is a C# value type (struct or enum).
    /// Value types use <c>Nullable&lt;T&gt;</c> for optional representation;
    /// reference types use <c>null</c> directly.
    /// </summary>
    public static bool IsValueType(IType type) => type.Kind is TypeKind.Struct or TypeKind.Enum;
}
