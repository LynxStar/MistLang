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
    private SymbolTable _symbolTable;
    private Dictionary<Storm.Runtime.SyntaxNode, IType> _expressionTypes;

    internal SemanticModel(
        MistCompilation compilation,
        IReadOnlyList<CompilationUnit> units,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        Scope globalScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType>? expressionTypes = null)
    {
        Compilation = compilation;
        Units = units;
        Asts = asts;
        GlobalScope = globalScope;
        _symbolTable = symbolTable;
        _expressionTypes = expressionTypes ?? new Dictionary<Storm.Runtime.SyntaxNode, IType>();
    }

    public MistCompilation Compilation { get; }
    public IScope GlobalScope { get; }
    public IReadOnlyList<CompilationUnit> Units { get; }
    public ImmutableArray<Storm.Runtime.SyntaxNode> Asts { get; }

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
        // First check the expression type map (populated by Pass 2).
        if (_expressionTypes.TryGetValue(node, out var type))
        {
            return type;
        }

        // Fallback: return the declared type of the symbol at this node, if any.
        var symbol = GetDeclaredSymbol(node);
        return symbol?.DeclaredType;
    }
}
