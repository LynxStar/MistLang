using System.Collections.Immutable;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Provides lazy, on-demand resolution of well-known .NET types for the C# backend.
/// Types are materialized only when user code references them, and results are cached
/// so that subsequent queries return the same instance.
///
/// <para>
/// After Task 42 of the Mist Standard Library spec, this resolver no longer
/// exposes <c>System.String</c> or <c>System.Array</c> as ambient types —
/// those are now stdlib types (<c>Standard.String</c>, <c>Standard.Array&lt;T&gt;</c>)
/// per Requirements 9.1, 9.2, 9.6. The resolver continues to expose
/// <c>Console</c>, <c>Math</c>, and <c>Convert</c> as ambient types per
/// Requirements 9.3, 9.4, 9.5.
/// </para>
///
/// <para>
/// All member signatures produced by this resolver reference Mist stdlib
/// types (via <see cref="IStdlibTypeProvider"/>) rather than legacy
/// fallback singletons,
/// satisfying Requirements 9.9 and 9.10.
/// </para>
/// </summary>
public sealed class CSharpAmbientTypeResolver : IAmbientTypeResolver
{
    /// <summary>Maps namespace name → set of type names available in that namespace.</summary>
    private readonly Dictionary<string, HashSet<string>> _namespaceTypes = new();

    /// <summary>Lazy factory functions keyed by (namespace, typeName).</summary>
    private readonly Dictionary<(string Namespace, string TypeName), Func<(IType, ImmutableArray<ISymbol>)>> _typeFactories = new();

    /// <summary>Materialized cache of resolved types and their members.</summary>
    private readonly Dictionary<(string Namespace, string TypeName), (IType Type, ImmutableArray<ISymbol> Members)> _cache = new();

    /// <summary>
    /// The stdlib type provider used to look up Mist types for member
    /// signatures (e.g., <c>Int32</c>, <c>String</c>, <c>Boolean</c>).
    /// Starts as <see cref="FallbackStdlibTypeProvider"/> and is upgraded
    /// to a model-backed provider by <see cref="SemanticModelBuilder"/>
    /// after Pass 1 registers all stdlib types.
    /// </summary>
    private IStdlibTypeProvider _stdlib;

    /// <summary>
    /// Creates a new ambient type resolver that references stdlib types
    /// via the given provider.
    /// </summary>
    /// <param name="stdlib">
    /// The stdlib type provider. When <c>null</c>, falls back to
    /// <see cref="FallbackStdlibTypeProvider"/> (legacy singletons),
    /// keeping tests that don't load stdlib working.
    /// </param>
    public CSharpAmbientTypeResolver(IStdlibTypeProvider? stdlib = null)
    {
        _stdlib = stdlib ?? FallbackStdlibTypeProvider.Instance;

        // Per Requirements 9.3, 9.4, 9.5: Console, Math, Convert remain
        // ambient types. Per Requirements 9.1, 9.2, 9.6: String and Array
        // are no longer exposed as ambient types — they are stdlib types.
        RegisterType("System", "Console", BuildConsole);
        RegisterType("System", "Math", BuildMath);
        RegisterType("System", "Convert", BuildConvert);
    }

    /// <inheritdoc />
    public bool HasNamespace(string namespaceName) => _namespaceTypes.ContainsKey(namespaceName);

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
        _stdlib = provider;
    }

    /// <inheritdoc />
    public IType? ResolveType(string namespaceName, string typeName)
    {
        if (_cache.TryGetValue((namespaceName, typeName), out var cached))
            return cached.Type;

        if (!_typeFactories.TryGetValue((namespaceName, typeName), out var factory))
            return null;

        var (type, members) = factory();
        _cache[(namespaceName, typeName)] = (type, members);
        return type;
    }

    /// <inheritdoc />
    public ImmutableArray<ISymbol> GetMembers(IType type)
    {
        foreach (var entry in _cache.Values)
        {
            if (ReferenceEquals(entry.Type, type))
                return entry.Members;
        }

        return [];
    }

    /// <inheritdoc />
    public ImmutableArray<string> GetTypeNamesInNamespace(string namespaceName)
    {
        if (_namespaceTypes.TryGetValue(namespaceName, out var types))
            return [.. types];

        return [];
    }

    private void RegisterType(string ns, string name, Func<(IType, ImmutableArray<ISymbol>)> factory)
    {
        if (!_namespaceTypes.ContainsKey(ns))
            _namespaceTypes[ns] = [];

        _namespaceTypes[ns].Add(name);
        _typeFactories[(ns, name)] = factory;
    }

    /// <summary>
    /// Creates a method symbol with <see cref="SymbolOrigin.Backend"/>.
    /// </summary>
    private static Symbol BuildMethodSymbol(
        string name, IType returnType, bool isStatic, IScope containingScope)
    {
        return new Symbol(
            name: name,
            kind: SymbolKind.Method,
            origin: SymbolOrigin.Backend,
            containingScope: containingScope,
            declaredType: returnType,
            declarationSpan: null);
    }

    /// <summary>
    /// Creates a parameter symbol with <see cref="SymbolOrigin.Backend"/>.
    /// </summary>
    private static Symbol BuildParameterSymbol(string name, IType type)
    {
        return new Symbol(
            name: name,
            kind: SymbolKind.Parameter,
            origin: SymbolOrigin.Backend,
            containingScope: new Scope(parent: null),
            declaredType: type,
            declarationSpan: null);
    }

    private (IType, ImmutableArray<ISymbol>) BuildConsole()
    {
        var consoleScope = new Scope(parent: null);
        var consoleType = new ClassType("Console", consoleScope, "System");

        var members = ImmutableArray.CreateBuilder<ISymbol>();

        // void WriteLine() — static
        members.Add(BuildMethodSymbol("WriteLine", VoidType.Instance,
            isStatic: true, consoleScope));

        // void WriteLine(string value) — static
        members.Add(BuildMethodSymbol("WriteLine", VoidType.Instance,
            isStatic: true, consoleScope));

        // void WriteLine(int value) — static
        members.Add(BuildMethodSymbol("WriteLine", VoidType.Instance,
            isStatic: true, consoleScope));

        // string ReadLine() — static (returns stdlib String)
        members.Add(BuildMethodSymbol("ReadLine", _stdlib.LookupStandardType("String"),
            isStatic: true, consoleScope));

        // void Write(string value) — static
        members.Add(BuildMethodSymbol("Write", VoidType.Instance,
            isStatic: true, consoleScope));

        return (consoleType, members.ToImmutable());
    }

    private (IType, ImmutableArray<ISymbol>) BuildMath()
    {
        var mathScope = new Scope(parent: null);
        var mathType = new ClassType("Math", mathScope, "System");

        var members = ImmutableArray.CreateBuilder<ISymbol>();

        // int Abs(int value) — static (returns stdlib Int32)
        members.Add(BuildMethodSymbol("Abs", _stdlib.LookupStandardType("Int32"),
            isStatic: true, mathScope));

        // int Max(int val1, int val2) — static
        members.Add(BuildMethodSymbol("Max", _stdlib.LookupStandardType("Int32"),
            isStatic: true, mathScope));

        // int Min(int val1, int val2) — static
        members.Add(BuildMethodSymbol("Min", _stdlib.LookupStandardType("Int32"),
            isStatic: true, mathScope));

        return (mathType, members.ToImmutable());
    }

    private (IType, ImmutableArray<ISymbol>) BuildConvert()
    {
        var convertScope = new Scope(parent: null);
        var convertType = new ClassType("Convert", convertScope, "System");

        var members = ImmutableArray.CreateBuilder<ISymbol>();

        // int ToInt32(string value) — static (returns stdlib Int32)
        members.Add(BuildMethodSymbol("ToInt32", _stdlib.LookupStandardType("Int32"),
            isStatic: true, convertScope));

        // string ToString(int value) — static (returns stdlib String)
        members.Add(BuildMethodSymbol("ToString", _stdlib.LookupStandardType("String"),
            isStatic: true, convertScope));

        return (convertType, members.ToImmutable());
    }
}
