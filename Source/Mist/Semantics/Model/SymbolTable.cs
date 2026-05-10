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
        SourceSpan? declarationSpan,
        bool isIntrinsic = false,
        bool hasBody = true)
    {
        Name = name;
        Kind = kind;
        Origin = origin;
        ContainingScope = containingScope;
        DeclaredType = declaredType;

        // Backend symbols have no source location in user code (R7.4).
        DeclarationSpan = origin == SymbolOrigin.Backend ? null : declarationSpan;

        IsIntrinsic = isIntrinsic;
        HasBody = hasBody;
    }

    public string Name { get; }
    public SymbolKind Kind { get; }
    public SymbolOrigin Origin { get; }
    public IScope ContainingScope { get; }
    public IType? DeclaredType { get; internal set; }
    public SourceSpan? DeclarationSpan { get; }

    /// <summary>
    /// Indicates the member's body is provided by the backend via the
    /// <c>IntrinsicEmissionTable</c> rather than by a Mist-bodied method
    /// (i.e., a member-level <c>intrinsic</c> modifier was present on its
    /// declaration). The flag is set by the semantic model builder and read
    /// by the emitter; it is immutable after construction.
    /// </summary>
    public bool IsIntrinsic { get; }

    /// <summary>
    /// Indicates whether the declaration provides a body. <c>true</c> for
    /// ordinary block-bodied methods, constructors, and operators;
    /// <c>false</c> for declarations that terminate with <c>;</c> (the
    /// <c>MethodNoBody</c> / <c>OperatorNoBody</c> grammar variants).
    /// Intrinsic members omit their bodies (Requirement 5.2); non-intrinsic
    /// members must provide one (Requirement 5.3, 5.4). The flag is set by
    /// the semantic model builder and read by downstream validation
    /// (<c>Pass1e_ValidateIntrinsicMarkers</c>, Task 9). Defaults to
    /// <c>true</c> so existing non-member callers (namespaces, fields,
    /// locals, parameters) remain correct without modification.
    /// </summary>
    public bool HasBody { get; }

    /// <summary>
    /// Property-specific metadata. Non-null only for Property and Indexer symbols.
    /// </summary>
    public PropertyInfo? PropertyMetadata { get; internal set; }

    /// <summary>
    /// Type parameters for generic method symbols. Empty for non-generic methods and non-method symbols.
    /// </summary>
    public IReadOnlyList<IType> TypeParameters { get; internal set; } = Array.Empty<IType>();

    /// <summary>
    /// Context with-clauses declared on this method symbol. Each entry records the
    /// resolved context type, the bound identifier, and the source span. Empty for
    /// non-method symbols and methods without with-clauses.
    /// </summary>
    public IReadOnlyList<ContextClauseInfo> WithClauses { get; internal set; } = Array.Empty<ContextClauseInfo>();

    /// <summary>
    /// Tracks the readonly classification of this symbol. Applies to fields, properties,
    /// indexers, methods, constructors, and parameters. Defaults to <see cref="Model.ReadonlyStatus.Unknown"/>
    /// until the semantic model builder or inference engine classifies it.
    /// </summary>
    public ReadonlyStatus ReadonlyStatus { get; internal set; } = ReadonlyStatus.Unknown;
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
