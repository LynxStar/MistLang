using System.Collections.Immutable;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Provides lazy, on-demand resolution of well-known .NET types for the C# backend.
/// Types are materialized only when user code references them, and results are cached
/// so that subsequent queries return the same instance.
/// </summary>
public sealed class CSharpAmbientTypeResolver : IAmbientTypeResolver
{
    /// <summary>Maps namespace name → set of type names available in that namespace.</summary>
    private readonly Dictionary<string, HashSet<string>> _namespaceTypes = new();

    /// <summary>Lazy factory functions keyed by (namespace, typeName).</summary>
    private readonly Dictionary<(string Namespace, string TypeName), Func<(IType, ImmutableArray<ISymbol>)>> _typeFactories = new();

    /// <summary>Materialized cache of resolved types and their members.</summary>
    private readonly Dictionary<(string Namespace, string TypeName), (IType Type, ImmutableArray<ISymbol> Members)> _cache = new();

    public CSharpAmbientTypeResolver()
    {
        RegisterType("System", "Console", BuildConsole);
        RegisterType("System", "String", BuildString);
        RegisterType("System", "Math", BuildMath);
        RegisterType("System", "Array", BuildArray);
        RegisterType("System", "Convert", BuildConvert);
    }

    /// <inheritdoc />
    public bool HasNamespace(string namespaceName) => _namespaceTypes.ContainsKey(namespaceName);

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

    private static (IType, ImmutableArray<ISymbol>) BuildConsole()
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

        // string ReadLine() — static
        members.Add(BuildMethodSymbol("ReadLine", StringType.Instance,
            isStatic: true, consoleScope));

        // void Write(string value) — static
        members.Add(BuildMethodSymbol("Write", VoidType.Instance,
            isStatic: true, consoleScope));

        return (consoleType, members.ToImmutable());
    }

    private static (IType, ImmutableArray<ISymbol>) BuildString()
    {
        var stringScope = new Scope(parent: null);
        var stringType = new ClassType("String", stringScope, "System");

        var members = ImmutableArray.CreateBuilder<ISymbol>();

        // int Length — instance (modeled as a method for v1 simplicity)
        members.Add(BuildMethodSymbol("Length", PrimitiveType.Int,
            isStatic: false, stringScope));

        // string Substring(int startIndex) — instance
        members.Add(BuildMethodSymbol("Substring", StringType.Instance,
            isStatic: false, stringScope));

        // bool Contains(string value) — instance
        members.Add(BuildMethodSymbol("Contains", PrimitiveType.Bool,
            isStatic: false, stringScope));

        return (stringType, members.ToImmutable());
    }

    private static (IType, ImmutableArray<ISymbol>) BuildMath()
    {
        var mathScope = new Scope(parent: null);
        var mathType = new ClassType("Math", mathScope, "System");

        var members = ImmutableArray.CreateBuilder<ISymbol>();

        // int Abs(int value) — static
        members.Add(BuildMethodSymbol("Abs", PrimitiveType.Int,
            isStatic: true, mathScope));

        // int Max(int val1, int val2) — static
        members.Add(BuildMethodSymbol("Max", PrimitiveType.Int,
            isStatic: true, mathScope));

        // int Min(int val1, int val2) — static
        members.Add(BuildMethodSymbol("Min", PrimitiveType.Int,
            isStatic: true, mathScope));

        return (mathType, members.ToImmutable());
    }

    private static (IType, ImmutableArray<ISymbol>) BuildArray()
    {
        var arrayScope = new Scope(parent: null);
        var arrayType = new ClassType("Array", arrayScope, "System");

        var members = ImmutableArray.CreateBuilder<ISymbol>();

        // int Length — instance
        members.Add(BuildMethodSymbol("Length", PrimitiveType.Int,
            isStatic: false, arrayScope));

        return (arrayType, members.ToImmutable());
    }

    private static (IType, ImmutableArray<ISymbol>) BuildConvert()
    {
        var convertScope = new Scope(parent: null);
        var convertType = new ClassType("Convert", convertScope, "System");

        var members = ImmutableArray.CreateBuilder<ISymbol>();

        // int ToInt32(string value) — static
        members.Add(BuildMethodSymbol("ToInt32", PrimitiveType.Int,
            isStatic: true, convertScope));

        // string ToString(int value) — static
        members.Add(BuildMethodSymbol("ToString", StringType.Instance,
            isStatic: true, convertScope));

        return (convertType, members.ToImmutable());
    }
}
