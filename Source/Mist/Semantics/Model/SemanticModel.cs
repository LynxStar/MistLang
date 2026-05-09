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
    private Dictionary<Storm.Runtime.SyntaxNode, ISymbol> _resolvedOperators;

    internal SemanticModel(
        MistCompilation compilation,
        IReadOnlyList<CompilationUnit> units,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        Scope globalScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType>? expressionTypes = null,
        TraitSatisfactionMap? traitSatisfactionMap = null,
        ExtensionRegistry? extensionRegistry = null)
    {
        Compilation = compilation;
        Units = units;
        Asts = asts;
        GlobalScope = globalScope;
        _symbolTable = symbolTable;
        _expressionTypes = expressionTypes ?? new Dictionary<Storm.Runtime.SyntaxNode, IType>();
        _resolvedOperators = new Dictionary<Storm.Runtime.SyntaxNode, ISymbol>();
        TraitSatisfactionMap = traitSatisfactionMap ?? new TraitSatisfactionMap();
        ExtensionRegistry = extensionRegistry ?? new ExtensionRegistry();
    }

    public MistCompilation Compilation { get; }
    public IScope GlobalScope { get; }
    public IReadOnlyList<CompilationUnit> Units { get; }
    public ImmutableArray<Storm.Runtime.SyntaxNode> Asts { get; }
    public TraitSatisfactionMap TraitSatisfactionMap { get; }
    public ExtensionRegistry ExtensionRegistry { get; }

    /// <summary>
    /// The symbol table that backs <see cref="AllSymbols"/>, exposed
    /// internally for stdlib-lookup helpers such as
    /// <c>SemanticModelBuilder.LookupQualifiedStdlibType</c> that need to
    /// walk symbol metadata rather than flatten via <see cref="AllSymbols"/>.
    /// </summary>
    internal SymbolTable SymbolTable => _symbolTable;

    /// <summary>
    /// The expression-type map populated by Pass 2, exposed internally so
    /// that tests can walk assignment targets and argument expressions
    /// without going through <see cref="GetTypeOf"/>'s fallback lookup.
    /// </summary>
    internal IReadOnlyDictionary<Storm.Runtime.SyntaxNode, IType> ExpressionTypes => _expressionTypes;

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

    public void SetTypeOf(Storm.Runtime.SyntaxNode node, IType type)
    {
        _expressionTypes[node] = type;
    }

    public void RecordResolvedOperator(Storm.Runtime.SyntaxNode node, ISymbol op)
    {
        _resolvedOperators[node] = op;
    }

    public ISymbol? GetResolvedOperator(Storm.Runtime.SyntaxNode node)
    {
        return _resolvedOperators.TryGetValue(node, out var op) ? op : null;
    }
}
