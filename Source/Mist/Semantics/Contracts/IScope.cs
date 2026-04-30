namespace Mist.Semantics.Contracts;

/// <summary>
/// A lexical scope containing symbol declarations.
/// </summary>
public interface IScope
{
    IScope? Parent { get; }
    IReadOnlyList<ISymbol> Members { get; }
    ISymbol? Lookup(string name);

    /// <summary>
    /// Returns all symbols with the given name from this scope and all ancestor scopes.
    /// </summary>
    IReadOnlyList<ISymbol> LookupAll(string name);
}
