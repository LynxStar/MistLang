using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// Indexes all extension blocks by receiver type for efficient lookup
/// during member resolution. Populated by the SemanticModelBuilder
/// and consumed by the TypeCheckerAnalyzer.
/// </summary>
public sealed class ExtensionRegistry
{
    // Keyed by IType (the resolved receiver type) using reference equality.
    private readonly Dictionary<IType, List<ExtensionBlockInfo>> _byReceiverType = new();

    // Universal extensions (receiver type = unknown) stored separately for fast access.
    private readonly List<ExtensionBlockInfo> _universalExtensions = new();

    /// <summary>
    /// Registers an extension block in the registry.
    /// Universal extensions are stored in both the universal list and the
    /// receiver-type dictionary for completeness.
    /// </summary>
    public void Register(ExtensionBlockInfo info)
    {
        if(info.IsUniversal)
        {
            _universalExtensions.Add(info);
        }

        if(!_byReceiverType.TryGetValue(info.ReceiverType, out var list))
        {
            list = new List<ExtensionBlockInfo>();
            _byReceiverType[info.ReceiverType] = list;
        }

        list.Add(info);
    }

    /// <summary>
    /// Returns all extension blocks whose receiver type is assignable from the given type.
    /// Includes universal extensions in the results.
    /// For generic extensions, includes them if the receiver type pattern could potentially
    /// match (exact matching happens during type inference in the type checker).
    /// </summary>
    public IReadOnlyList<ExtensionBlockInfo> GetApplicableExtensions(IType valueType)
    {
        var results = new List<ExtensionBlockInfo>();

        // Always include universal extensions — they apply to all types.
        results.AddRange(_universalExtensions);

        foreach(var (receiverType, extensions) in _byReceiverType)
        {
            // Skip universal extensions (already added above).
            if(receiverType is UnknownType)
                continue;

            if(IsApplicable(receiverType, valueType))
            {
                results.AddRange(extensions);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns extension blocks applicable to a union type (A | B | ...).
    /// An extension is included only if it is applicable to ALL constituents of the union.
    /// Universal extensions (receiver type = unknown) are always included.
    /// </summary>
    public IReadOnlyList<ExtensionBlockInfo> GetApplicableExtensionsForUnion(IReadOnlyList<IType> constituents)
    {
        var results = new List<ExtensionBlockInfo>();

        // Universal extensions always apply to union-typed values.
        results.AddRange(_universalExtensions);

        foreach(var (receiverType, extensions) in _byReceiverType)
        {
            // Skip universal extensions (already added above).
            if(receiverType is UnknownType)
                continue;

            // An extension is applicable to the union only if it is applicable
            // to every constituent type.
            var applicableToAll = true;
            foreach(var constituent in constituents)
            {
                if(!IsApplicable(receiverType, constituent))
                {
                    applicableToAll = false;
                    break;
                }
            }

            if(applicableToAll)
            {
                results.AddRange(extensions);
            }
        }

        return results;
    }

    /// <summary>
    /// Returns only universal extensions (receiver type = unknown).
    /// </summary>
    public IReadOnlyList<ExtensionBlockInfo> UniversalExtensions => _universalExtensions;

    /// <summary>
    /// Returns extension blocks whose receiver type exactly matches the given type.
    /// Used for static extension member lookup where assignability is not considered.
    /// </summary>
    public IReadOnlyList<ExtensionBlockInfo> GetExactExtensions(IType type)
    {
        if(_byReceiverType.TryGetValue(type, out var list))
        {
            return list;
        }

        return Array.Empty<ExtensionBlockInfo>();
    }

    /// <summary>
    /// Returns all extension block groups keyed by receiver type.
    /// Used for duplicate signature detection across extension blocks.
    /// </summary>
    public IEnumerable<KeyValuePair<IType, List<ExtensionBlockInfo>>> AllGroups => _byReceiverType;

    /// <summary>
    /// Finds the extension block info that provides a property member with the given name
    /// for the given receiver type. Returns null if no extension property matches.
    /// Used by the C# emitter for call-site rewriting of extension properties.
    /// </summary>
    public ExtensionBlockInfo? FindExtensionPropertyBlock(IType receiverType, string propertyName)
    {
        var applicable = GetApplicableExtensions(receiverType);
        foreach(var ext in applicable)
        {
            var member = ext.MemberScope.Lookup(propertyName);
            if(member is not null && member.Kind == SymbolKind.Property)
            {
                return ext;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the extension block info that provides a static method member with the given name
    /// for the given receiver type. Returns null if no static extension method matches.
    /// Used by the C# emitter for call-site rewriting of static extension methods.
    /// </summary>
    public ExtensionBlockInfo? FindStaticExtensionMethodBlock(IType receiverType, string methodName)
    {
        var exact = GetExactExtensions(receiverType);
        foreach(var ext in exact)
        {
            var member = ext.MemberScope.Lookup(methodName);
            if(member is not null && member.Kind == SymbolKind.Method)
            {
                return ext;
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the ambient extension block info that provides a method member with the given name
    /// for the given receiver type. Returns null if no ambient extension method matches.
    /// Used by the C# emitter for call-site rewriting of ambient extension method calls
    /// as static method invocations.
    /// </summary>
    public ExtensionBlockInfo? FindAmbientExtensionMethodBlock(IType receiverType, string methodName)
    {
        var applicable = GetApplicableExtensions(receiverType);
        foreach(var ext in applicable)
        {
            if(!ext.IsAmbient)
                continue;

            var member = ext.MemberScope.Lookup(methodName);
            if(member is not null && member.Kind == SymbolKind.Method)
            {
                return ext;
            }
        }
        return null;
    }

    /// <summary>
    /// Determines whether an extension with the given receiver type is applicable
    /// to a value of the given type.
    ///
    /// After Phase A type interning, all CLR types projected from assembly metadata
    /// resolve to canonical singleton instances. Mist-defined types are singletons
    /// from the scope chain. Therefore reference equality is the primary matching
    /// mechanism. Structural comparison is only needed for:
    ///   - <see cref="ConstructedType"/> (not interned; multiple instances of the
    ///     same logical constructed type may exist at different call sites)
    ///   - <see cref="ArrayType"/> (same reasoning as ConstructedType)
    ///   - Generic pattern matching (extensions on open generic types are potentially
    ///     applicable to any concrete instantiation; actual inference happens later)
    ///   - Inheritance walking (base type chain + implemented abstracts)
    /// </summary>
    private static bool IsApplicable(IType extensionReceiverType, IType valueType)
    {
        // ── 1. Reference equality (covers all interned types) ───────────────
        if(ReferenceEquals(extensionReceiverType, valueType))
            return true;

        // ── 2. Open generic type definitions ────────────────────────────────
        // An extension declared on an uninstantiated generic type (e.g., the
        // ClassType for List<T> with TypeParameters.Count > 0) is potentially
        // applicable to any value — type inference narrows later.
        if(IsOpenGenericDefinition(extensionReceiverType))
            return true;

        // ── 3. Generic pattern (ConstructedType with type parameters) ───────
        // An extension receiver like IEnumerable<T> (a ConstructedType whose
        // type arguments include TypeParameterType) is a pattern that may match
        // any value type satisfying the shape.
        if(extensionReceiverType is ConstructedType ctPattern && ContainsTypeParameter(ctPattern))
            return IsPatternApplicable(ctPattern, valueType);

        // ── 4. ConstructedType value → generic definition match ─────────────
        // If the value is a concrete constructed type (e.g., List<int>), the
        // extension's receiver may target the generic definition (List<T>).
        if(valueType is ConstructedType ctValue)
        {
            if(ReferenceEquals(extensionReceiverType, ctValue.GenericDefinition))
                return true;
        }

        // ── 5. ArrayType structural match ───────────────────────────────────
        // Two ArrayType instances for the same element type are applicable.
        if(extensionReceiverType is ArrayType extArray && valueType is ArrayType valArray)
            return TypesEqual(extArray.ElementType, valArray.ElementType);

        // ── 6. Non-generic System.Collections.IEnumerable ───────────────────
        // This interface is intentionally excluded from projected Abstracts lists,
        // so inheritance walking cannot discover it. LINQ's Cast<T> and OfType<T>
        // target this interface. Any type that could plausibly implement
        // IEnumerable (classes, structs, arrays, other abstracts) is considered
        // potentially applicable — the C# compiler enforces the actual constraint.
        if(IsNonGenericIEnumerable(extensionReceiverType)
            && valueType is ClassType or StructType or ArrayType or AbstractType or ConstructedType)
        {
            return true;
        }

        // ── 7. Inheritance: base type chain (classes) ───────────────────────
        if(valueType is ClassType classValue && classValue.BaseType is not null)
        {
            if(IsApplicable(extensionReceiverType, classValue.BaseType))
                return true;
        }

        // ── 8. Inheritance: implemented abstracts ───────────────────────────
        // Walk the Abstracts list for class, struct, abstract, and constructed value types.
        var implementedAbstracts = valueType switch
        {
            ClassType ct => ct.Abstracts,
            StructType st => st.Abstracts,
            AbstractType at => at.Abstracts,
            ConstructedType ctVal => ctVal.GenericDefinition switch
            {
                ClassType ctDef => ctDef.Abstracts,
                StructType stDef => stDef.Abstracts,
                AbstractType atDef => atDef.Abstracts,
                _ => null
            },
            _ => null
        };

        if(implementedAbstracts is { Count: > 0 })
        {
            if(ImplementsAbstract(implementedAbstracts, extensionReceiverType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the type is an open generic definition (has declared type
    /// parameters but is not a ConstructedType — it's the uninstantiated definition itself).
    /// </summary>
    private static bool IsOpenGenericDefinition(IType type) => type switch
    {
        ClassType c => c.TypeParameters.Count > 0,
        StructType s => s.TypeParameters.Count > 0,
        AbstractType a => a.TypeParameters.Count > 0,
        _ => false,
    };

    /// <summary>
    /// Returns true if the type is the non-generic <c>System.Collections.IEnumerable</c>.
    /// </summary>
    private static bool IsNonGenericIEnumerable(IType type) =>
        type is AbstractType { TypeParameters.Count: 0, DisplayName: "IEnumerable", ContainingNamespace: "System.Collections" };

    /// <summary>
    /// Checks whether a generic extension receiver pattern (a ConstructedType containing
    /// TypeParameterType arguments) could match the given value type.
    /// </summary>
    private static bool IsPatternApplicable(ConstructedType receiverPattern, IType valueType)
    {
        // For arrays, only array-compatible abstract patterns apply.
        if(valueType is ArrayType)
        {
            return receiverPattern.GenericDefinition is AbstractType abstractDef
                && receiverPattern.TypeArguments.Count == 1
                && IsArrayCompatibleAbstract(abstractDef);
        }

        // For concrete constructed types, check if the generic definitions match.
        if(valueType is ConstructedType ctValue)
        {
            if(ReferenceEquals(receiverPattern.GenericDefinition, ctValue.GenericDefinition))
                return true;

            // Also check if the value type's generic definition implements the
            // receiver pattern's generic definition (e.g., List<int> implements
            // IEnumerable<T> because List<T> : IEnumerable<T>).
            var valueAbstracts = ctValue.GenericDefinition switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                AbstractType at => at.Abstracts,
                _ => null
            };
            if(valueAbstracts is not null && ImplementsAbstract(valueAbstracts, receiverPattern.GenericDefinition))
                return true;

            return false;
        }

        // For plain class/struct/abstract types, check if they or their base types
        // implement the pattern's generic definition.
        var implementedAbstracts = valueType switch
        {
            ClassType ct => ct.Abstracts,
            StructType st => st.Abstracts,
            AbstractType at => at.Abstracts,
            _ => null
        };

        if(implementedAbstracts is not null
            && ImplementsAbstract(implementedAbstracts, receiverPattern.GenericDefinition))
            return true;

        // Walk base class chain for classes.
        if(valueType is ClassType classVal && classVal.BaseType is not null)
            return IsPatternApplicable(receiverPattern, classVal.BaseType);

        return false;
    }

    /// <summary>
    /// Returns true if a constructed type contains any <see cref="TypeParameterType"/>
    /// in its type arguments, indicating it is a generic pattern rather than a
    /// concrete instantiation.
    /// </summary>
    private static bool ContainsTypeParameter(ConstructedType constructedType)
    {
        foreach(var arg in constructedType.TypeArguments)
        {
            if(arg is TypeParameterType)
                return true;
            if(arg is ConstructedType nested && ContainsTypeParameter(nested))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursively walks the implemented abstracts list (and their inherited abstracts)
    /// to determine if the extension receiver type is among them.
    /// Uses reference equality as the primary check (Phase A ensures canonical instances
    /// for CLR-projected types). Falls back to ConstructedType structural matching
    /// since ConstructedType instances are not interned.
    /// </summary>
    private static bool ImplementsAbstract(
        IReadOnlyList<IType> abstracts,
        IType extensionReceiverType,
        HashSet<IType>? visited = null)
    {
        foreach(var abs in abstracts)
        {
            // Reference equality covers interned AbstractType and same-instance
            // ConstructedType (e.g., when the same object appears in both lists).
            if(ReferenceEquals(abs, extensionReceiverType))
                return true;

            // ConstructedType structural match: two IEnumerable<int> instances
            // from different projection sites are logically the same type.
            if(abs is ConstructedType absCt && extensionReceiverType is ConstructedType targetCt)
            {
                if(ConstructedTypesEqual(absCt, targetCt))
                    return true;
            }

            // An extension targeting a generic definition (e.g., IEnumerable<T> with
            // TypeParameters.Count > 0) matches any constructed form of that definition
            // in the implements list.
            if(abs is ConstructedType absCtForDef
                && ReferenceEquals(absCtForDef.GenericDefinition, extensionReceiverType))
                return true;

            // Conversely, if the extension receiver is a ConstructedType pattern and
            // the abstract in the list is its generic definition.
            if(extensionReceiverType is ConstructedType extCt
                && ReferenceEquals(abs, extCt.GenericDefinition))
                return true;

            // Recursively check inherited abstracts.
            visited ??= new HashSet<IType>(ReferenceEqualityComparer.Instance);
            if(!visited.Add(abs))
                continue;

            var inheritedAbstracts = abs switch
            {
                AbstractType at => at.Abstracts,
                ConstructedType ct when ct.GenericDefinition is AbstractType atDef => atDef.Abstracts,
                _ => null
            };

            if(inheritedAbstracts is not null && ImplementsAbstract(inheritedAbstracts, extensionReceiverType, visited))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Structural equality for two <see cref="ConstructedType"/> instances.
    /// Required because ConstructedType is not interned — multiple instances
    /// representing the same logical type (e.g., <c>IEnumerable&lt;int&gt;</c>)
    /// can exist at different sites.
    /// </summary>
    private static bool ConstructedTypesEqual(ConstructedType a, ConstructedType b)
    {
        if(!ReferenceEquals(a.GenericDefinition, b.GenericDefinition))
            return false;

        if(a.TypeArguments.Count != b.TypeArguments.Count)
            return false;

        for(var i = 0; i < a.TypeArguments.Count; i++)
        {
            if(!TypesEqual(a.TypeArguments[i], b.TypeArguments[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Recursive type equality check. Uses reference equality for atomic types
    /// (interned after Phase A) and structural comparison for compound types
    /// (ConstructedType, ArrayType) that are not interned.
    /// </summary>
    private static bool TypesEqual(IType a, IType b)
    {
        if(ReferenceEquals(a, b))
            return true;

        return (a, b) switch
        {
            (ConstructedType ca, ConstructedType cb) => ConstructedTypesEqual(ca, cb),
            (ArrayType aa, ArrayType ab) => TypesEqual(aa.ElementType, ab.ElementType),
            _ => false
        };
    }

    /// <summary>
    /// Returns true if the given abstract type is one that .NET arrays implement:
    /// IEnumerable, ICollection, IList, IReadOnlyCollection, IReadOnlyList.
    /// </summary>
    private static bool IsArrayCompatibleAbstract(AbstractType abs)
    {
        var name = abs.DisplayName;
        return name is "IEnumerable" or "ICollection" or "IList"
            or "IReadOnlyCollection" or "IReadOnlyList";
    }
}