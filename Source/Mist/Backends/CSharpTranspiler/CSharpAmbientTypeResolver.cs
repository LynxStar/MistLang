using System.Collections.Immutable;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Provides lazy, on-demand resolution of well-known .NET types for the C# backend.
/// Types are materialized only when user code references them, and results are cached
/// so that subsequent queries return the same instance.
///
/// <para>
/// This resolver currently has no registered types. Console, Math, and Convert
/// have been migrated to the Standard Library as intrinsic types declared in
/// <c>Source/Standard/Bootstrap/</c>. The resolver infrastructure remains in
/// place for future use.
/// </para>
/// </summary>
public sealed class CSharpAmbientTypeResolver : IAmbientTypeResolver
{
    /// <summary>Maps namespace name → set of type names available in that namespace.</summary>
    private readonly Dictionary<string, HashSet<string>> namespaceTypes = new();

    /// <summary>Lazy factory functions keyed by (namespace, typeName).</summary>
    private readonly Dictionary<(string Namespace, string TypeName), Func<(IType, ImmutableArray<ISymbol>)>> typeFactories = new();

    /// <summary>Materialized cache of resolved types and their members.</summary>
    private readonly Dictionary<(string Namespace, string TypeName), (IType Type, ImmutableArray<ISymbol> Members)> cache = new();

    /// <summary>
    /// The stdlib type provider used to look up Mist types for member
    /// signatures (e.g., <c>Int32</c>, <c>String</c>, <c>Boolean</c>).
    /// Starts as <see cref="FallbackStdlibTypeProvider"/> and is upgraded
    /// to a model-backed provider by <see cref="SemanticModelBuilder"/>
    /// after Pass 1 registers all stdlib types.
    /// </summary>
    private IStdlibTypeProvider stdlib;

    /// <summary>
    /// Creates a new ambient type resolver. Currently registers no types —
    /// Console, Math, and Convert have been migrated to the Standard Library.
    /// The resolver remains available for future ambient type registrations.
    /// </summary>
    /// <param name="stdlib">
    /// The stdlib type provider. When <c>null</c>, falls back to
    /// <see cref="FallbackStdlibTypeProvider"/> (legacy singletons),
    /// keeping tests that don't load stdlib working.
    /// </param>
    public CSharpAmbientTypeResolver(IStdlibTypeProvider? stdlib = null)
    {
        this.stdlib = stdlib ?? FallbackStdlibTypeProvider.Instance;
    }

    /// <inheritdoc />
    public bool HasNamespace(string namespaceName) => namespaceTypes.ContainsKey(namespaceName);

    /// <summary>
    /// Upgrades the stdlib type provider used by lazy type factories.
    /// Called by <see cref="Mist.Semantics.Model.SemanticModelBuilder"/>
    /// after Pass 1 registers all stdlib types so that ambient-type member
    /// signatures (e.g., <c>Math.Abs</c> returning <c>Int32</c>) reference
    /// the real stdlib <see cref="IType"/> instances instead of the
    /// <see cref="FallbackStdlibTypeProvider"/> singletons. Only factories
    /// that have not yet been materialized benefit from the upgrade; already-
    /// cached types keep their original provider's instances.
    /// </summary>
    internal void UpdateStdlibProvider(IStdlibTypeProvider provider)
    {
        stdlib = provider;
    }

    /// <inheritdoc />
    public IType? ResolveType(string namespaceName, string typeName)
    {
        if(cache.TryGetValue((namespaceName, typeName), out var cached))
        {
            return cached.Type;
        }

        if(!typeFactories.TryGetValue((namespaceName, typeName), out var factory))
        {
            return null;
        }

        var (type, members) = factory();
        cache[(namespaceName, typeName)] = (type, members);
        return type;
    }

    /// <inheritdoc />
    public ImmutableArray<ISymbol> GetMembers(IType type)
    {
        foreach(var entry in cache.Values)
        {
            if(ReferenceEquals(entry.Type, type))
            {
                return entry.Members;
            }
        }

        return [];
    }

    /// <inheritdoc />
    public ImmutableArray<string> GetTypeNamesInNamespace(string namespaceName)
    {
        if(namespaceTypes.TryGetValue(namespaceName, out var types))
        {
            return [.. types];
        }

        return [];
    }

    /// <inheritdoc />
    public IReadOnlyList<AmbientExtensionMethodInfo> GetExtensionMethodsInNamespace(string namespaceName)
        => Array.Empty<AmbientExtensionMethodInfo>();

    private void RegisterType(string ns, string name, Func<(IType, ImmutableArray<ISymbol>)> factory)
    {
        if(!namespaceTypes.ContainsKey(ns))
        {
            namespaceTypes[ns] = [];
        }

        namespaceTypes[ns].Add(name);
        typeFactories[(ns, name)] = factory;
    }
}