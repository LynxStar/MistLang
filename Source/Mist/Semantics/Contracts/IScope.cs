namespace Mist.Semantics.Contracts;

/// <summary>
/// A lexical scope containing symbol declarations.
/// </summary>
public interface IScope
{
    IScope? Parent { get; }
    IReadOnlyList<ISymbol> Members { get; }
    ISymbol? Lookup(string name);
}
