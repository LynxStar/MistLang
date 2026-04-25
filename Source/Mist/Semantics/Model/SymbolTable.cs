using Mist.Common.SourceText;
using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// A concrete implementation of <see cref="ISymbol"/>.
/// </summary>
public sealed class Symbol : ISymbol
{
    public Symbol(
        string name,
        SymbolKind kind,
        IScope containingScope,
        IType? declaredType,
        SourceSpan? declarationSpan)
    {
        Name = name;
        Kind = kind;
        ContainingScope = containingScope;
        DeclaredType = declaredType;
        DeclarationSpan = declarationSpan;
    }

    public string Name { get; }
    public SymbolKind Kind { get; }
    public IScope ContainingScope { get; }
    public IType? DeclaredType { get; }
    public SourceSpan? DeclarationSpan { get; }
}

/// <summary>
/// A concrete implementation of <see cref="IType"/>.
/// </summary>
public sealed class MistType : IType
{
    public MistType(string displayName, TypeKind kind)
    {
        DisplayName = displayName;
        Kind = kind;
    }

    public string DisplayName { get; }
    public TypeKind Kind { get; }
}

/// <summary>
/// Manages symbol registration and lookup during semantic model construction.
/// </summary>
public sealed class SymbolTable
{
    private readonly List<ISymbol> _allSymbols = [];
    private readonly Dictionary<Storm.Runtime.SyntaxNode, ISymbol> _nodeToSymbol = [];
    private readonly Dictionary<ISymbol, Storm.Runtime.SyntaxNode> _symbolToNode = [];

    public void Register(ISymbol symbol, Storm.Runtime.SyntaxNode declaringNode)
    {
        _allSymbols.Add(symbol);
        _nodeToSymbol[declaringNode] = symbol;
        _symbolToNode[symbol] = declaringNode;
    }

    public IReadOnlyList<ISymbol> AllSymbols => _allSymbols;

    public ISymbol? GetDeclaredSymbol(Storm.Runtime.SyntaxNode node)
        => _nodeToSymbol.GetValueOrDefault(node);

    public Storm.Runtime.SyntaxNode? GetDeclaringNode(ISymbol symbol)
        => _symbolToNode.GetValueOrDefault(symbol);
}
