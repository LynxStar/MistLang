using System.Collections.Immutable;
using Mist.Backends.CSharpTranspiler;

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
    /// Resolve a type by namespace, simple name, and generic arity. Returns null if not found.
    /// When <paramref name="typeArgumentCount"/> is specified (>= 0), the resolver picks the
    /// overload with exactly that many type parameters (e.g., <c>Func`2</c> for arity 2).
    /// Falls back to <see cref="ResolveType(string, string)"/> when arity is not relevant.
    /// </summary>
    IType? ResolveType(string namespaceName, string typeName, int typeArgumentCount)
    {
        return ResolveType(namespaceName, typeName);
    }

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

    /// <summary>
    /// Returns all C# extension methods declared in the given namespace.
    /// Returns an empty list if the namespace has no extension methods.
    /// </summary>
    IReadOnlyList<AmbientExtensionMethodInfo> GetExtensionMethodsInNamespace(string namespaceName);
}