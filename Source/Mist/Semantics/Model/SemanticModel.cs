using System.Collections.Immutable;
using Mist.Driver.Contracts;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// Immutable implementation of <see cref="ISemanticModel"/>.
/// All collections are exposed as read-only; no public mutation API after construction.
/// </summary>
public sealed class SemanticModel : ISemanticModel
{
    private readonly SymbolTable _symbolTable;

    internal SemanticModel(
        Compilation compilation,
        IReadOnlyList<CompilationUnit> units,
        Scope globalScope,
        SymbolTable symbolTable)
    {
        Compilation = compilation;
        Units = units;
        GlobalScope = globalScope;
        _symbolTable = symbolTable;
    }

    public Compilation Compilation { get; }
    public IScope GlobalScope { get; }
    public IReadOnlyList<CompilationUnit> Units { get; }

    public ImmutableArray<ISymbol> AllSymbols => [.. _symbolTable.AllSymbols];

    public Storm.Runtime.SyntaxNode GetDeclaringNode(ISymbol symbol)
    {
        return _symbolTable.GetDeclaringNode(symbol)
            ?? throw new InvalidOperationException($"No declaring node found for symbol '{symbol.Name}'.");
    }

    public ISymbol? GetDeclaredSymbol(Storm.Runtime.SyntaxNode node)
    {
        return _symbolTable.GetDeclaredSymbol(node);
    }

    public IType? GetTypeOf(Storm.Runtime.SyntaxNode node)
    {
        // Type resolution is deferred to the Mist-language spec.
        // For now, return the declared type of the symbol at this node, if any.
        var symbol = GetDeclaredSymbol(node);
        return symbol?.DeclaredType;
    }
}
