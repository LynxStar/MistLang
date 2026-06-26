using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Provides bidirectional mapping between CLR types and Standard Library types.
/// The reversed map is derived from <see cref="CSharpTypeMap.BackingTypes"/>
/// (single source of truth — no separate mapping table).
///
/// <para>
/// Used by the Assembly Type Provider to substitute intrinsic CLR types
/// (e.g., <c>System.Int32</c>, <c>System.String</c>) with their corresponding
/// Standard Library types (e.g., <c>Standard.Int32</c>, <c>Standard.String</c>)
/// during type projection.
/// </para>
/// </summary>
internal sealed class IntrinsicTypeMap
{
    /// <summary>
    /// Maps CLR type names (both C# keywords like "int" and full CLR names like
    /// "System.Int32") to their Mist Standard Library fully-qualified names
    /// (e.g., "Standard.Int32").
    /// </summary>
    private readonly Dictionary<string, string> _clrToMist;

    /// <summary>
    /// Well-known mapping from C# keywords to their full CLR type names.
    /// Used to generate additional reverse-map entries so that both
    /// "int" and "System.Int32" resolve to "Standard.Int32".
    /// </summary>
    private static readonly Dictionary<string, string> KeywordToClrFullName = new(StringComparer.Ordinal)
    {
        ["int"] = "System.Int32",
        ["uint"] = "System.UInt32",
        ["long"] = "System.Int64",
        ["ulong"] = "System.UInt64",
        ["short"] = "System.Int16",
        ["ushort"] = "System.UInt16",
        ["byte"] = "System.Byte",
        ["sbyte"] = "System.SByte",
        ["float"] = "System.Single",
        ["double"] = "System.Double",
        ["decimal"] = "System.Decimal",
        ["bool"] = "System.Boolean",
        ["char"] = "System.Char",
        ["string"] = "System.String",
        ["void"] = "System.Void",
    };

    /// <summary>
    /// Additional inbound-only CLR → Mist projections that intentionally have
    /// no entry in <see cref="CSharpTypeMap.BackingTypes"/> because their
    /// outbound C# emission is governed separately (the Backing_Type_Map is the
    /// single source of truth for emission). The Assembly Type Provider uses
    /// these entries to recognize the CLR types on inbound parameter, return,
    /// and field positions and project them to the corresponding Mist Standard
    /// Library type, mirroring the <c>Task</c>/<c>Task&lt;T&gt;</c> intake.
    /// </summary>
    private static readonly Dictionary<string, string> AdditionalClrToMist = new(StringComparer.Ordinal)
    {
        // System.Threading.Tasks.ValueTask (non-generic) and ValueTask<T>
        // (generic, arity stripped to "ValueTask") both share this CLR name
        // after generic-arity stripping, so a single entry projects both to
        // the Mist ValueTask / ValueTask<TResult> linear type — exactly as the
        // "System.Threading.Tasks.Task" entry projects Task and Task<T>.
        ["System.Threading.Tasks.ValueTask"] = "Standard.ValueTask",
    };

    public IntrinsicTypeMap()
    {
        _clrToMist = BuildReversedMap();
    }

    /// <summary>
    /// Returns the Standard Library fully-qualified type name for a CLR type name,
    /// or <c>null</c> if no mapping exists.
    /// </summary>
    /// <param name="clrTypeName">
    /// A CLR type name — either a C# keyword (e.g., "int"), a full CLR name
    /// (e.g., "System.Int32"), or a qualified name from BackingTypes
    /// (e.g., "System.Threading.Tasks.Task").
    /// </param>
    /// <returns>
    /// The fully-qualified Mist Standard Library name (e.g., "Standard.Int32"),
    /// or <c>null</c> if the CLR type is not an intrinsic.
    /// </returns>
    public string? GetMistTypeName(string clrTypeName)
    {
        return _clrToMist.TryGetValue(clrTypeName, out var mistName) ? mistName : null;
    }

    /// <summary>
    /// Recursively substitutes intrinsic types in a type reference,
    /// including nested generic type arguments at all levels.
    /// </summary>
    /// <param name="projectedType">The type to substitute intrinsics in.</param>
    /// <param name="stdlib">The stdlib type provider for resolving Standard Library types.</param>
    /// <returns>
    /// The type with all intrinsic CLR types replaced by their Standard Library equivalents.
    /// If the type is not an intrinsic and has no generic arguments containing intrinsics,
    /// it is returned unchanged.
    /// </returns>
    public IType SubstituteIntrinsics(IType projectedType, IStdlibTypeProvider stdlib)
    {
        // Check if the projected type itself is an intrinsic that should be substituted.
        var mistName = TryGetMistNameFromProjectedType(projectedType);
        if(mistName is not null)
        {
            var simpleName = GetSimpleName(mistName);
            try
            {
                return stdlib.LookupStandardType(simpleName);
            }
            catch(InvalidOperationException)
            {
                // FallbackStdlibTypeProvider doesn't have this type yet.
                // Return the projected type unchanged; it will be substituted
                // when the type is re-resolved after UpdateStdlibProvider.
                return projectedType;
            }
        }

        // For constructed generic types, recursively substitute type arguments.
        if(projectedType is ConstructedType ct)
        {
            return SubstituteConstructedType(ct, stdlib);
        }

        // Non-intrinsic, non-generic type — return unchanged.
        return projectedType;
    }

    /// <summary>
    /// Substitutes intrinsic types within a constructed generic type's type arguments.
    /// If any type argument is substituted, a new <see cref="ConstructedType"/> is created
    /// with the substituted arguments. If no substitution occurs, the original is returned.
    /// </summary>
    private ConstructedType SubstituteConstructedType(ConstructedType ct, IStdlibTypeProvider stdlib)
    {
        var substitutedDefinition = SubstituteIntrinsics(ct.GenericDefinition, stdlib);

        var anyChanged = !ReferenceEquals(substitutedDefinition, ct.GenericDefinition);
        var substitutedArgs = new IType[ct.TypeArguments.Count];

        for(var i = 0; i < ct.TypeArguments.Count; i++)
        {
            substitutedArgs[i] = SubstituteIntrinsics(ct.TypeArguments[i], stdlib);
            if(!ReferenceEquals(substitutedArgs[i], ct.TypeArguments[i]))
            {
                anyChanged = true;
            }
        }

        if(!anyChanged)
        {
            return ct;
        }

        return new ConstructedType(
            substitutedDefinition,
            substitutedArgs,
            ct.DependentArguments);
    }

    /// <summary>
    /// Attempts to determine the Mist Standard Library name for a projected type
    /// by examining its namespace and display name.
    /// </summary>
    private string? TryGetMistNameFromProjectedType(IType type)
    {
        // A type that is already a Standard Library type (IsBackendBacked + Standard namespace)
        // does not need substitution — it's already the correct type.
        switch(type)
        {
            case StructType { IsBackendBacked: true, ContainingNamespace: "Standard" }:
            case ClassType { IsBackendBacked: true, ContainingNamespace: "Standard" }:
                return null;

            case StructType st:
                {
                    var qualifiedName = st.ContainingNamespace is not null
                        ? $"{st.ContainingNamespace}.{st.DisplayName}"
                        : st.DisplayName;
                    return GetMistTypeName(qualifiedName);
                }

            case ClassType ct:
                {
                    var qualifiedName = ct.ContainingNamespace is not null
                        ? $"{ct.ContainingNamespace}.{ct.DisplayName}"
                        : ct.DisplayName;
                    return GetMistTypeName(qualifiedName);
                }

            case AbstractType it:
                {
                    var qualifiedName = it.ContainingNamespace is not null
                        ? $"{it.ContainingNamespace}.{it.DisplayName}"
                        : it.DisplayName;
                    return GetMistTypeName(qualifiedName);
                }

            default:
                return null;
        }
    }

    /// <summary>
    /// Extracts the simple name from a fully-qualified Mist Standard Library name.
    /// E.g., "Standard.Int32" → "Int32", "Standard.String" → "String".
    /// </summary>
    private static string GetSimpleName(string fullyQualifiedMistName)
    {
        var dotIndex = fullyQualifiedMistName.LastIndexOf('.');
        return dotIndex >= 0
            ? fullyQualifiedMistName[(dotIndex + 1)..]
            : fullyQualifiedMistName;
    }

    /// <summary>
    /// Builds the reversed map by reading <see cref="CSharpTypeMap.BackingTypes"/> directly.
    /// For each entry in BackingTypes where the key starts with "Standard." (the canonical
    /// fully-qualified entries), we create a reverse entry (C# name → Mist name).
    /// For C# keyword entries (e.g., "int"), we also add the full CLR name
    /// (e.g., "System.Int32") as an additional key.
    /// Legacy entries (keys without "Standard." prefix) are skipped since they are
    /// duplicates of the canonical entries for backward compatibility.
    /// </summary>
    private static Dictionary<string, string> BuildReversedMap()
    {
        var reversed = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach(var (mistName, csharpName) in CSharpTypeMap.BackingTypes)
        {
            // Only process canonical "Standard.*" entries to avoid duplicates
            // from the legacy keyword entries (e.g., "int" → "int").
            if(!mistName.StartsWith("Standard.", StringComparison.Ordinal))
            {
                continue;
            }

            // Map the C# name (keyword or qualified) → Mist name
            reversed.TryAdd(csharpName, mistName);

            // For C# keywords, also add the full CLR name
            if(KeywordToClrFullName.TryGetValue(csharpName, out var clrFullName))
            {
                reversed.TryAdd(clrFullName, mistName);
            }
        }

        // Add inbound-only CLR → Mist projections that have no Backing_Type_Map
        // entry (their outbound emission is governed elsewhere). These let the
        // Assembly Type Provider recognize the CLR types on inbound positions.
        foreach(var (clrName, mistName) in AdditionalClrToMist)
        {
            reversed.TryAdd(clrName, mistName);
        }

        return reversed;
    }
}