using System.Collections.Immutable;

namespace Mist.Semantics.Contracts;

/// <summary>
/// Provides on-demand resolution of types from external sources (backend runtime,
/// future libraries). The semantic model builder queries this during name resolution
/// instead of eagerly loading all external types upfront.
/// </summary>
public interface IAmbientTypeResolver
{
    /// <summary>Does this namespace exist in the external type world?</summary>
    bool HasNamespace(string namespaceName);

    /// <summary>
    /// Resolve a type by namespace and simple name. Returns null if not found.
    /// The returned IType and its member ISymbols are tagged with SymbolOrigin.Backend
    /// and are cached — subsequent calls for the same type return the same instance.
    /// </summary>
    IType? ResolveType(string namespaceName, string typeName);

    /// <summary>
    /// Get the members (methods, fields, constructors) of a previously resolved type.
    /// All returned symbols have SymbolOrigin.Backend.
    /// </summary>
    ImmutableArray<ISymbol> GetMembers(IType type);

    /// <summary>
    /// List all type names available in a namespace. Used during name resolution
    /// when searching imported namespaces for an unqualified type name.
    /// </summary>
    ImmutableArray<string> GetTypeNamesInNamespace(string namespaceName);
}
