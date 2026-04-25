using System.Collections.Immutable;
using Mist.Driver.Contracts;

namespace Mist.Semantics.Contracts;

/// <summary>
/// An immutable representation of Mist source code enriched with resolved symbols,
/// types, scopes, and other semantic information.
/// </summary>
public interface ISemanticModel
{
    Compilation Compilation { get; }
    IScope GlobalScope { get; }
    IReadOnlyList<CompilationUnit> Units { get; }

    /// <summary>
    /// Returns the AST node that declares the given symbol.
    /// </summary>
    Storm.Runtime.SyntaxNode GetDeclaringNode(ISymbol symbol);

    /// <summary>
    /// Returns the symbol declared by the given AST node, if any.
    /// </summary>
    ISymbol? GetDeclaredSymbol(Storm.Runtime.SyntaxNode node);

    /// <summary>
    /// Returns the type of the given AST node expression, if any.
    /// </summary>
    IType? GetTypeOf(Storm.Runtime.SyntaxNode node);

    ImmutableArray<ISymbol> AllSymbols { get; }
}
