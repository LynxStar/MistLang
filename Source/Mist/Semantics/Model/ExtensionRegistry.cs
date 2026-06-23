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
        if (info.IsUniversal)
        {
            _universalExtensions.Add(info);
        }

        if (!_byReceiverType.TryGetValue(info.ReceiverType, out var list))
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

        foreach (var (receiverType, extensions) in _byReceiverType)
        {
            // Skip universal extensions (already added above).
            if (receiverType is UnknownType)
                continue;

            if (IsApplicable(receiverType, valueType))
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

        foreach (var (receiverType, extensions) in _byReceiverType)
        {
            // Skip universal extensions (already added above).
            if (receiverType is UnknownType)
                continue;

            // An extension is applicable to the union only if it is applicable
            // to every constituent type.
            var applicableToAll = true;
            foreach (var constituent in constituents)
            {
                if (!IsApplicable(receiverType, constituent))
                {
                    applicableToAll = false;
                    break;
                }
            }

            if (applicableToAll)
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
        if (_byReceiverType.TryGetValue(type, out var list))
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
        foreach (var ext in applicable)
        {
            var member = ext.MemberScope.Lookup(propertyName);
            if (member is not null && member.Kind == SymbolKind.Property)
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
        foreach (var ext in exact)
        {
            var member = ext.MemberScope.Lookup(methodName);
            if (member is not null && member.Kind == SymbolKind.Method)
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
        foreach (var ext in applicable)
        {
            if (!ext.IsAmbient)
                continue;

            var member = ext.MemberScope.Lookup(methodName);
            if (member is not null && member.Kind == SymbolKind.Method)
            {
                return ext;
            }
        }
        return null;
    }

    /// <summary>
    /// Determines whether an extension with the given receiver type is applicable
    /// to a value of the given type.
    /// </summary>
    private static bool IsApplicable(IType extensionReceiverType, IType valueType)
    {
        // Exact reference match (most common case).
        if (ReferenceEquals(extensionReceiverType, valueType))
            return true;

        // Structural equality for ClassType: ambient extension receivers are projected
        // before Pass 2 resolves user code, so the receiver may be a disconnected shell
        // (different instance) from the cached type the user's variable resolves to.
        // Match by DisplayName + ContainingNamespace to bridge this gap.
        if (extensionReceiverType is ClassType extClass && valueType is ClassType valClass
            && extClass.TypeParameters.Count == 0 && valClass.TypeParameters.Count == 0)
        {
            if (extClass.DisplayName == valClass.DisplayName
                && extClass.ContainingNamespace == valClass.ContainingNamespace)
                return true;
        }

        // Structural equality for StructType (same reasoning as ClassType above).
        if (extensionReceiverType is StructType extStruct && valueType is StructType valStruct
            && extStruct.TypeParameters.Count == 0 && valStruct.TypeParameters.Count == 0)
        {
            if (extStruct.DisplayName == valStruct.DisplayName
                && extStruct.ContainingNamespace == valStruct.ContainingNamespace)
                return true;
        }

        // Array type structural match: two ArrayType instances with the same
        // element type are applicable (ArrayType uses reference equality, so
        // different instances of int[] won't match via ReferenceEquals).
        if (extensionReceiverType is ArrayType extArray && valueType is ArrayType valArray)
        {
            return ReferenceEquals(extArray.ElementType, valArray.ElementType);
        }

        // If the extension receiver is a generic type definition (has type parameters),
        // include it as a potential match — actual type inference happens in the type checker.
        if (extensionReceiverType is ClassType c && c.TypeParameters.Count > 0)
            return true;
        if (extensionReceiverType is StructType s && s.TypeParameters.Count > 0)
            return true;
        if (extensionReceiverType is AbstractType ai && ai.TypeParameters.Count > 0)
            return true;

        // If the extension receiver is a ConstructedType containing type parameters
        // (e.g., IEnumerable<T> from a generic extension method), check if the value
        // type could potentially satisfy the pattern.
        if (extensionReceiverType is ConstructedType ctReceiver && ContainsTypeParameter(ctReceiver))
        {
            // For ArrayType values, only interfaces that arrays implement are applicable.
            if (valueType is ArrayType)
            {
                return ctReceiver.GenericDefinition is AbstractType atGenDef
                    && ctReceiver.TypeArguments.Count == 1
                    && IsArrayCompatibleInterface(atGenDef);
            }

            // For ConstructedType values, check if the generic definitions match.
            if (valueType is ConstructedType ctValue)
            {
                if (ReferenceEquals(ctReceiver.GenericDefinition, ctValue.GenericDefinition))
                    return true;

                // Structural fallback: when the generic definitions are projected from
                // different instances (ambient extension scanner vs. type resolver cache),
                // compare by DisplayName + ContainingNamespace.
                return GenericDefinitionsMatch(ctReceiver.GenericDefinition, ctValue.GenericDefinition);
            }

            // For other types, include as potential match (type inference will filter).
            return true;
        }

        // If the value type is a constructed type, check if the extension receiver
        // matches the generic definition.
        if (valueType is ConstructedType constructed)
        {
            if (ReferenceEquals(extensionReceiverType, constructed.GenericDefinition))
                return true;

            // Structural fallback for disconnected shell instances.
            if (GenericDefinitionsMatch(extensionReceiverType, constructed.GenericDefinition))
                return true;
        }

        // Check base type chain for class types.
        if (valueType is ClassType classType && classType.BaseType is not null)
        {
            if (IsApplicable(extensionReceiverType, classType.BaseType))
                return true;
        }

        // Check interface implementations (structural matching).
        if (valueType is ClassType ct)
        {
            if (ImplementsInterfaceStructural(ct.Interfaces, extensionReceiverType))
                return true;
        }

        // Check interface implementations for struct types.
        if (valueType is StructType st)
        {
            if (ImplementsInterfaceStructural(st.Interfaces, extensionReceiverType))
                return true;
        }

        // Check interface-typed receivers: when the value IS an interface type,
        // check if it structurally matches the extension receiver or inherits from it.
        // This enables .NET extension methods targeting interfaces (e.g., LINQ on
        // IEnumerable, DI on IServiceCollection) to resolve on interface-typed variables.
        if (valueType is AbstractType valueAbstract)
        {
            if (InterfaceMatches(valueAbstract, extensionReceiverType))
                return true;

            // Check inherited interfaces of the value interface.
            if (valueAbstract.ParentAbstractTypes.Count > 0
                && ImplementsInterfaceStructural(valueAbstract.ParentAbstractTypes, extensionReceiverType))
                return true;
        }

        // Handle ConstructedAbstractType as value type (e.g., IEnumerable<Int32>).
        // LINQ extension methods target IEnumerable<T>, so when a variable is typed as
        // IEnumerable<Int32>, we need to check if the extension receiver matches.
        if (valueType is ConstructedAbstractType catValue)
        {
            // Direct definition match: extension on IEnumerable<T> applies to IEnumerable<Int32>
            if (extensionReceiverType is ConstructedType ctExtReceiver && ContainsTypeParameter(ctExtReceiver))
            {
                // Match by display name alone (cross-namespace bridging: CLR IEnumerable
                // in System.Collections.Generic matches stdlib IEnumerable in Standard)
                if (ctExtReceiver.GenericDefinition is AbstractType extGenDef
                    && extGenDef.DisplayName == catValue.GenericAbstractDefinition.DisplayName)
                    return true;

                if (GenericDefinitionsMatch(ctExtReceiver.GenericDefinition, catValue.GenericAbstractDefinition))
                    return true;
            }

            // Plain AbstractType receiver matches the generic definition
            if (extensionReceiverType is AbstractType atExtReceiver)
            {
                if (InterfaceMatches(catValue.GenericAbstractDefinition, atExtReceiver))
                    return true;
            }

            // Check parent abstract types of the generic definition
            if (catValue.GenericAbstractDefinition.ParentAbstractTypes.Count > 0
                && ImplementsInterfaceStructural(catValue.GenericAbstractDefinition.ParentAbstractTypes, extensionReceiverType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if a constructed type contains any <see cref="TypeParameterType"/>
    /// in its type arguments. This indicates the type is a generic pattern (e.g.,
    /// IEnumerable&lt;T&gt;) that should be considered potentially applicable to any value type.
    /// </summary>
    private static bool ContainsTypeParameter(ConstructedType constructedType)
    {
        foreach (var arg in constructedType.TypeArguments)
        {
            if (arg is TypeParameterType)
                return true;
            if (arg is ConstructedType nested && ContainsTypeParameter(nested))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Recursively checks whether the given extension receiver type is implemented by any
    /// interface in the provided list (or in any interface's own inherited interfaces).
    /// Uses structural matching (DisplayName + ContainingNamespace for AbstractType,
    /// GenericDefinition + TypeArguments for ConstructedType wrapping an interface).
    /// </summary>
    private static bool ImplementsInterfaceStructural(
        IReadOnlyList<IType> interfaces,
        IType extensionReceiverType,
        HashSet<IType>? visited = null)
    {
        foreach (var iface in interfaces)
        {
            if (InterfaceMatches(iface, extensionReceiverType))
                return true;

            // Recursively check inherited interfaces.
            visited ??= new HashSet<IType>(ReferenceEqualityComparer.Instance);
            if (!visited.Add(iface))
                continue;

            var inheritedInterfaces = iface switch
            {
                AbstractType at => at.ParentAbstractTypes,
                ConstructedType ct when ct.GenericDefinition is AbstractType atDef => atDef.ParentAbstractTypes,
                _ => null
            };

            if (inheritedInterfaces is not null && ImplementsInterfaceStructural(inheritedInterfaces, extensionReceiverType, visited))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Determines whether two interface type references represent the same interface.
    /// Handles AbstractType (by DisplayName + ContainingNamespace) and ConstructedType
    /// wrapping an interface (by GenericDefinition identity + TypeArguments structural equality).
    /// </summary>
    private static bool InterfaceMatches(IType iface, IType target)
    {
        if (ReferenceEquals(iface, target))
            return true;

        // Both are plain AbstractType: compare by name + namespace.
        if (iface is AbstractType ifaceAt && target is AbstractType targetAt)
        {
            return ifaceAt.DisplayName == targetAt.DisplayName
                && ifaceAt.ContainingNamespace == targetAt.ContainingNamespace;
        }

        // Both are ConstructedType wrapping an interface: compare generic definition + type args.
        if (iface is ConstructedType ifaceCt && target is ConstructedType targetCt)
        {
            if (ifaceCt.GenericDefinition is AbstractType && targetCt.GenericDefinition is AbstractType)
            {
                if (ifaceCt.DisplayName == targetCt.DisplayName)
                    return true;

                // Same generic definition + same type arguments.
                if (InterfaceMatches(ifaceCt.GenericDefinition, targetCt.GenericDefinition)
                    && ifaceCt.TypeArguments.Count == targetCt.TypeArguments.Count)
                {
                    for (var i = 0; i < ifaceCt.TypeArguments.Count; i++)
                    {
                        if (!ReferenceEquals(ifaceCt.TypeArguments[i], targetCt.TypeArguments[i])
                            && ifaceCt.TypeArguments[i].DisplayName != targetCt.TypeArguments[i].DisplayName)
                            return false;
                    }
                    return true;
                }
            }
        }

        // iface is AbstractType, target is ConstructedType with that interface as generic def (or vice versa).
        // This handles the case where the extension is registered on a non-generic interface
        // but the implementation list has it wrapped in a constructed type, or vice versa.
        if (iface is AbstractType plainIface && target is ConstructedType targetCtForPlain)
        {
            if (targetCtForPlain.GenericDefinition is AbstractType targetGenDef)
                return plainIface.DisplayName == targetGenDef.DisplayName
                    && plainIface.ContainingNamespace == targetGenDef.ContainingNamespace;
        }
        if (iface is ConstructedType ifaceCtForPlain && target is AbstractType plainTarget)
        {
            if (ifaceCtForPlain.GenericDefinition is AbstractType ifaceGenDef)
                return ifaceGenDef.DisplayName == plainTarget.DisplayName
                    && ifaceGenDef.ContainingNamespace == plainTarget.ContainingNamespace;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the given interface type is one that .NET arrays implement:
    /// IEnumerable, ICollection, IList, IReadOnlyCollection, IReadOnlyList.
    /// </summary>
    private static bool IsArrayCompatibleInterface(AbstractType iface)
    {
        var name = iface.DisplayName;
        return name is "IEnumerable" or "ICollection" or "IList"
            or "IReadOnlyCollection" or "IReadOnlyList";
    }

    /// <summary>
    /// Structural comparison of two generic type definitions by DisplayName and
    /// ContainingNamespace. Used when the ambient extension scanner projects a
    /// receiver type before the main type resolver caches it, producing a
    /// disconnected shell that won't satisfy ReferenceEquals.
    /// </summary>
    private static bool GenericDefinitionsMatch(IType a, IType b)
    {
        if (ReferenceEquals(a, b))
            return true;

        return (a, b) switch
        {
            (ClassType ca, ClassType cb) =>
                ca.DisplayName == cb.DisplayName && ca.ContainingNamespace == cb.ContainingNamespace,
            (StructType sa, StructType sb) =>
                sa.DisplayName == sb.DisplayName && sa.ContainingNamespace == sb.ContainingNamespace,
            (AbstractType ia, AbstractType ib) =>
                ia.DisplayName == ib.DisplayName && ia.ContainingNamespace == ib.ContainingNamespace,
            _ => false
        };
    }
}
