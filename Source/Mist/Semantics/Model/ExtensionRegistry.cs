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
    /// Determines whether an extension with the given receiver type is applicable
    /// to a value of the given type.
    /// </summary>
    private static bool IsApplicable(IType extensionReceiverType, IType valueType)
    {
        // Exact reference match (most common case).
        if (ReferenceEquals(extensionReceiverType, valueType))
            return true;

        // If the extension receiver is a generic type definition (has type parameters),
        // include it as a potential match — actual type inference happens in the type checker.
        if (HasTypeParameters(extensionReceiverType))
            return true;

        // If the value type is a constructed type, check if the extension receiver
        // matches the generic definition.
        if (valueType is ConstructedType constructed &&
            ReferenceEquals(extensionReceiverType, constructed.GenericDefinition))
            return true;

        // Check base type chain for class types.
        if (valueType is ClassType classType && classType.BaseType is not null)
        {
            if (IsApplicable(extensionReceiverType, classType.BaseType))
                return true;
        }

        // Check interface implementations.
        if (valueType is ClassType ct)
        {
            foreach (var iface in ct.Interfaces)
            {
                if (ReferenceEquals(extensionReceiverType, iface))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if the type has type parameters (is a generic type definition).
    /// </summary>
    private static bool HasTypeParameters(IType type)
    {
        return type switch
        {
            ClassType c => c.TypeParameters.Count > 0,
            StructType s => s.TypeParameters.Count > 0,
            InterfaceType i => i.TypeParameters.Count > 0,
            _ => false
        };
    }
}
