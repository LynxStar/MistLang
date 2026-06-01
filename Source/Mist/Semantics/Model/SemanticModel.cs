using System.Collections.Immutable;
using Mist.Driver.Contracts;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

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
    private Dictionary<ISymbol, RefinementPredicate> _parameterRefinements;
    private HashSet<ISymbol> _guardNeededParameters;
    private Dictionary<Storm.Runtime.SyntaxNode, List<LiveTaskInfo>> _liveTasksByScope;
    private readonly Dictionary<Storm.Runtime.SyntaxNode, IReadOnlyList<IType>> _inferredTypeArguments = new();

    internal SemanticModel(
        MistCompilation compilation,
        IReadOnlyList<CompilationUnit> units,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        Scope globalScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType>? expressionTypes = null,
        TraitSatisfactionMap? traitSatisfactionMap = null,
        ExtensionRegistry? extensionRegistry = null,
        Dictionary<ISymbol, RefinementPredicate>? parameterRefinements = null,
        Dictionary<Storm.Runtime.SyntaxNode, List<LiveTaskInfo>>? liveTasksByScope = null)
    {
        Compilation = compilation;
        Units = units;
        Asts = asts;
        GlobalScope = globalScope;
        _symbolTable = symbolTable;
        _expressionTypes = expressionTypes ?? new Dictionary<Storm.Runtime.SyntaxNode, IType>();
        _resolvedOperators = new Dictionary<Storm.Runtime.SyntaxNode, ISymbol>();
        _parameterRefinements = parameterRefinements ?? new Dictionary<ISymbol, RefinementPredicate>();
        _guardNeededParameters = new HashSet<ISymbol>();
        _liveTasksByScope = liveTasksByScope ?? new Dictionary<Storm.Runtime.SyntaxNode, List<LiveTaskInfo>>();
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
            // When the cached expression type is a TypeParameterType, the type checker
            // may have refined the variable's DeclaredType through type argument inference.
            // Check if this node references a local variable whose DeclaredType has been
            // updated to a concrete type, and prefer that over the stale cached type.
            if (type is TypeParameterType)
            {
                var refinedType = TryGetRefinedTypeForLocalVariable(node);
                if (refinedType is not null)
                    return refinedType;
            }

            // When the cached expression type is a UnionType, the type checker may have
            // narrowed the variable's DeclaredType through ??= flow narrowing. Check if
            // the symbol's DeclaredType has been updated to a narrower type.
            if (type is UnionType)
            {
                var refinedType = TryGetRefinedTypeForLocalVariable(node);
                if (refinedType is not null)
                    return refinedType;
            }

            return type;
        }

        // Fallback: return the declared type of the symbol at this node, if any.
        var fallbackSymbol = GetDeclaredSymbol(node);
        return fallbackSymbol?.DeclaredType;
    }

    /// <summary>
    /// When a cached expression type is a TypeParameterType, checks if the node
    /// (or its underlying identifier) references a local variable whose DeclaredType
    /// has been refined by type argument inference to a concrete type.
    /// </summary>
    private IType? TryGetRefinedTypeForLocalVariable(Storm.Runtime.SyntaxNode node)
    {
        // Direct case: the node itself has a registered symbol (identifier node).
        var symbol = GetDeclaredSymbol(node);
        if (symbol is not null
            && symbol.Kind == SymbolKind.LocalVariable
            && symbol.DeclaredType is not null
            && symbol.DeclaredType is not TypeParameterType)
        {
            return symbol.DeclaredType;
        }

        // PostfixNode case: check if the target identifier references a refined local.
        if (node is PostfixNode postfixNode && postfixNode.Suffix.Length == 0)
        {
            var targetSymbol = GetDeclaredSymbol(postfixNode.Target);
            if (targetSymbol is not null
                && targetSymbol.Kind == SymbolKind.LocalVariable
                && targetSymbol.DeclaredType is not null
                && targetSymbol.DeclaredType is not TypeParameterType)
            {
                return targetSymbol.DeclaredType;
            }
        }

        // PostfixExpressionUnaryNode case: delegate to the PostfixNode.
        if (node is PostfixExpressionUnaryNode postfixExprNode)
        {
            var postfix = postfixExprNode.Primary;
            if (postfix.Suffix.Length == 0)
            {
                var targetSymbol = GetDeclaredSymbol(postfix.Target);
                if (targetSymbol is not null
                    && targetSymbol.Kind == SymbolKind.LocalVariable
                    && targetSymbol.DeclaredType is not null
                    && targetSymbol.DeclaredType is not TypeParameterType)
                {
                    return targetSymbol.DeclaredType;
                }
            }
            else
            {
                // For postfix expressions with suffixes, check if the PostfixNode's
                // type was updated by the type checker.
                if (_expressionTypes.TryGetValue(postfix, out var postfixType)
                    && postfixType is not TypeParameterType)
                {
                    return postfixType;
                }
            }
        }

        // Wrapper case: walk down expression wrappers to find the underlying identifier.
        // This handles ExpressionNode → AssignmentNode → ... → PostfixExpressionUnaryNode
        // → PostfixNode (target = identifier, no suffixes).
        if (node is ExpressionNode expr)
        {
            var assignment = expr.AsAssignmentOrNull();
            if (assignment is null) return null; // Lambda form has no refined identifier.
            if (assignment.Value is not null) return null;

            // Navigate through VoidCoalescing to the underlying LogicalOr.
            var voidCoalescing = assignment.Target;
            if (voidCoalescing.LogicalOrs.Length > 0) return null;
            return TryGetRefinedTypeFromLogicalOr(voidCoalescing.Left);
        }

        return null;
    }

    private IType? TryGetRefinedTypeFromLogicalOr(LogicalOrNode logicalOr)
    {
        if (logicalOr.Right is not null) return null;

        var logicalAnd = logicalOr.Left;
        if (logicalAnd.Right is not null) return null;

        var equality = logicalAnd.Left;
        if (equality.Right is not null) return null;

        var relational = equality.Left;
        if (relational is not RelationalComparisonRelationalNode cmp) return null;
        if (cmp.Right is not null) return null;

        var additive = cmp.Left;
        if (additive.Right is not null) return null;

        var multiplicative = additive.Left;
        if (multiplicative.Right is not null) return null;

        var unary = multiplicative.Left;
        if (unary is not PostfixExpressionUnaryNode postfixExpr) return null;

        var postfix = postfixExpr.Primary;

        // For a simple identifier with no suffixes, check the target's symbol.
        if (postfix.Suffix.Length == 0)
        {
            var targetSymbol = GetDeclaredSymbol(postfix.Target);
            if (targetSymbol is not null
                && targetSymbol.Kind == SymbolKind.LocalVariable
                && targetSymbol.DeclaredType is not null
                && targetSymbol.DeclaredType is not TypeParameterType)
            {
                return targetSymbol.DeclaredType;
            }
        }
        else
        {
            // For postfix expressions with suffixes (e.g., method calls),
            // check if the PostfixNode's type was updated by the type checker.
            if (_expressionTypes.TryGetValue(postfix, out var postfixType)
                && postfixType is not TypeParameterType)
            {
                return postfixType;
            }
        }

        return null;
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

    public RefinementPredicate? GetParameterRefinement(ISymbol parameterSymbol)
    {
        return _parameterRefinements.TryGetValue(parameterSymbol, out var predicate) ? predicate : null;
    }

    public void RecordGuardNeeded(ISymbol parameterSymbol)
    {
        _guardNeededParameters.Add(parameterSymbol);
    }

    public bool IsGuardElidable(ISymbol parameterSymbol)
    {
        // A guard is elidable if the parameter has a refinement constraint but no call site
        // required a runtime guard (all call sites passed validated constants or arguments
        // with provably-satisfying refinement types).
        return _parameterRefinements.ContainsKey(parameterSymbol)
            && !_guardNeededParameters.Contains(parameterSymbol);
    }

    /// <summary>
    /// Returns the list of live (un-awaited, un-returned) tasks for the given
    /// block scope node, or an empty list if no live tasks were recorded.
    /// The emitter uses this to generate scope-exit await code.
    /// </summary>
    public IReadOnlyList<LiveTaskInfo> GetLiveTasks(Storm.Runtime.SyntaxNode blockScopeNode)
    {
        return _liveTasksByScope.TryGetValue(blockScopeNode, out var tasks)
            ? tasks
            : Array.Empty<LiveTaskInfo>();
    }

    /// <summary>
    /// Returns all block scope nodes that have live tasks recorded.
    /// </summary>
    internal IReadOnlyDictionary<Storm.Runtime.SyntaxNode, List<LiveTaskInfo>> LiveTasksByScope => _liveTasksByScope;

    /// <inheritdoc />
    public void RecordInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode, IReadOnlyList<IType> inferredTypeArgs)
    {
        _inferredTypeArguments[callSiteNode] = inferredTypeArgs;
    }

    /// <inheritdoc />
    public IReadOnlyList<IType>? GetInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode)
    {
        return _inferredTypeArguments.TryGetValue(callSiteNode, out var args) ? args : null;
    }
}
