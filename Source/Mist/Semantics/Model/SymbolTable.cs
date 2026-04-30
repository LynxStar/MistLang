using Mist.Common.Text;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// A concrete implementation of <see cref="ISymbol"/>.
/// </summary>
public sealed class Symbol : ISymbol
{
    public Symbol(
        string name,
        SymbolKind kind,
        SymbolOrigin origin,
        IScope containingScope,
        IType? declaredType,
        SourceSpan? declarationSpan)
    {
        Name = name;
        Kind = kind;
        Origin = origin;
        ContainingScope = containingScope;
        DeclaredType = declaredType;

        // Backend symbols have no source location in user code (R7.4).
        DeclarationSpan = origin == SymbolOrigin.Backend ? null : declarationSpan;
    }

    public string Name { get; }
    public SymbolKind Kind { get; }
    public SymbolOrigin Origin { get; }
    public IScope ContainingScope { get; }
    public IType? DeclaredType { get; internal set; }
    public SourceSpan? DeclarationSpan { get; }

    /// <summary>
    /// Property-specific metadata. Non-null only for Property and Indexer symbols.
    /// </summary>
    public PropertyInfo? PropertyMetadata { get; internal set; }

    /// <summary>
    /// Type parameters for generic method symbols. Empty for non-generic methods and non-method symbols.
    /// </summary>
    public IReadOnlyList<TypeParameterType> TypeParameters { get; internal set; } = Array.Empty<TypeParameterType>();
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
    private List<ISymbol> _allSymbols = [];
    private Dictionary<Storm.Runtime.SyntaxNode, ISymbol> _nodeToSymbol = [];
    private Dictionary<ISymbol, Storm.Runtime.SyntaxNode> _symbolToNode = [];

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
