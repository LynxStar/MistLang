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
    private SymbolTable symbolTable;
    private Dictionary<Storm.Runtime.SyntaxNode, IType> expressionTypes;
    private Dictionary<Storm.Runtime.SyntaxNode, ISymbol> resolvedOperators;
    private Dictionary<ISymbol, RefinementPredicate> parameterRefinements;
    private HashSet<ISymbol> guardNeededParameters;
    private Dictionary<Storm.Runtime.SyntaxNode, List<DiscardedTaskInfo>> discardedTasksByScope;
    private readonly Dictionary<Storm.Runtime.SyntaxNode, IReadOnlyList<IType>> inferredTypeArguments = new();
    private readonly Dictionary<Storm.Runtime.SyntaxNode, IType> implicitCloneSites = new();
    private readonly Dictionary<Storm.Runtime.SyntaxNode, AsConversionKind> asConversionKinds = new();

    internal SemanticModel(
        MistCompilation compilation,
        IReadOnlyList<CompilationUnit> units,
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        Scope globalScope,
        SymbolTable symbolTable,
        Dictionary<Storm.Runtime.SyntaxNode, IType>? expressionTypes = null,
        ExtensionRegistry? extensionRegistry = null,
        ConformanceMap? conformanceMap = null,
        Dictionary<ISymbol, RefinementPredicate>? parameterRefinements = null,
        Dictionary<Storm.Runtime.SyntaxNode, List<DiscardedTaskInfo>>? discardedTasksByScope = null)
    {
        Compilation = compilation;
        Units = units;
        Asts = asts;
        GlobalScope = globalScope;
        this.symbolTable = symbolTable;
        this.expressionTypes = expressionTypes ?? new Dictionary<Storm.Runtime.SyntaxNode, IType>();
        resolvedOperators = new Dictionary<Storm.Runtime.SyntaxNode, ISymbol>();
        this.parameterRefinements = parameterRefinements ?? new Dictionary<ISymbol, RefinementPredicate>();
        guardNeededParameters = new HashSet<ISymbol>();
        this.discardedTasksByScope = discardedTasksByScope ?? new Dictionary<Storm.Runtime.SyntaxNode, List<DiscardedTaskInfo>>();
        ExtensionRegistry = extensionRegistry ?? new ExtensionRegistry();
        ConformanceMap = conformanceMap ?? new ConformanceMap();
    }

    public MistCompilation Compilation { get; }
    public IScope GlobalScope { get; }
    public IReadOnlyList<CompilationUnit> Units { get; }
    public ImmutableArray<Storm.Runtime.SyntaxNode> Asts { get; }
    public ExtensionRegistry ExtensionRegistry { get; }
    public ConformanceMap ConformanceMap { get; }

    /// <summary>
    /// The symbol table that backs <see cref="AllSymbols"/>, exposed
    /// internally for stdlib-lookup helpers such as
    /// <c>SemanticModelBuilder.LookupQualifiedStdlibType</c> that need to
    /// walk symbol metadata rather than flatten via <see cref="AllSymbols"/>.
    /// </summary>
    internal SymbolTable SymbolTable => symbolTable;

    /// <summary>
    /// The expression-type map populated by Pass 2, exposed internally so
    /// that tests can walk assignment targets and argument expressions
    /// without going through <see cref="GetTypeOf"/>'s fallback lookup.
    /// </summary>
    internal IReadOnlyDictionary<Storm.Runtime.SyntaxNode, IType> ExpressionTypes => expressionTypes;

    public ImmutableArray<ISymbol> AllSymbols => [.. symbolTable.AllSymbols];

    public Storm.Runtime.SyntaxNode GetDeclaringNode(ISymbol symbol)
    {
        return symbolTable.GetDeclaringNode(symbol)
            ?? throw new InvalidOperationException($"No declaring node found for symbol '{symbol.Name}'.");
    }

    public ISymbol? GetDeclaredSymbol(Storm.Runtime.SyntaxNode node)
    {
        return symbolTable.GetDeclaredSymbol(node);
    }

    public IType? GetTypeOf(Storm.Runtime.SyntaxNode node)
    {
        // First check the expression type map (populated by Pass 2).
        if(expressionTypes.TryGetValue(node, out var type))
        {
            // When the cached expression type is a TypeParameterType, the type checker
            // may have refined the variable's DeclaredType through type argument inference.
            // Check if this node references a local variable whose DeclaredType has been
            // updated to a concrete type, and prefer that over the stale cached type.
            if(type is TypeParameterType)
            {
                var refinedType = TryGetRefinedTypeForLocalVariable(node);
                if(refinedType is not null)
                {
                    return refinedType;
                }
            }

            // When the cached expression type is a UnionType, the type checker may have
            // narrowed the variable's DeclaredType through ??= flow narrowing. Check if
            // the symbol's DeclaredType has been updated to a narrower type.
            if(type is UnionType)
            {
                var refinedType = TryGetRefinedTypeForLocalVariable(node);
                if(refinedType is not null)
                {
                    return refinedType;
                }
            }

            // When the cached type is a non-union concrete type but the local variable's
            // DeclaredType has been widened to a union (e.g., by a non-provable `as`
            // expression producing T | void), prefer the widened type. This ensures
            // downstream member access sees the union and requires `?.` to unwrap.
            if(type is not UnionType && type is not TypeParameterType)
            {
                var refinedType = TryGetRefinedTypeForLocalVariable(node);
                if(refinedType is UnionType)
                {
                    return refinedType;
                }
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
        if(symbol is not null
            && symbol.Kind == SymbolKind.LocalVariable
            && symbol.DeclaredType is not null
            && symbol.DeclaredType is not TypeParameterType)
        {
            return symbol.DeclaredType;
        }

        // PostfixNode case: check if the target identifier references a refined local.
        if(node is PostfixNode postfixNode && postfixNode.Suffix.Length == 0)
        {
            var targetSymbol = GetDeclaredSymbol(postfixNode.Target);
            if(targetSymbol is not null
                && targetSymbol.Kind == SymbolKind.LocalVariable
                && targetSymbol.DeclaredType is not null
                && targetSymbol.DeclaredType is not TypeParameterType)
            {
                return targetSymbol.DeclaredType;
            }
        }

        // PostfixExpressionUnaryNode case: delegate to the PostfixNode.
        if(node is PostfixExpressionUnaryNode postfixExprNode)
        {
            var postfix = postfixExprNode.Primary;
            if(postfix.Suffix.Length == 0)
            {
                var targetSymbol = GetDeclaredSymbol(postfix.Target);
                if(targetSymbol is not null
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
                if(expressionTypes.TryGetValue(postfix, out var postfixType)
                    && postfixType is not TypeParameterType)
                {
                    return postfixType;
                }
            }
        }

        // Wrapper case: walk down expression wrappers to find the underlying identifier.
        // This handles ExpressionNode → AssignmentNode → ... → PostfixExpressionUnaryNode
        // → PostfixNode (target = identifier, no suffixes).
        if(node is ExpressionNode expr)
        {
            var assignment = expr.AsAssignmentOrNull();
            if(assignment is null)
            {
                return null; // Lambda form has no refined identifier.
            }

            if(assignment.Value is not null)
            {
                return null;
            }

            // Navigate through VoidCoalescing to the underlying LogicalOr.
            var voidCoalescing = assignment.Target;
            if(voidCoalescing.LogicalOrs.Length > 0)
            {
                return null;
            }

            return TryGetRefinedTypeFromLogicalOr(voidCoalescing.Left);
        }

        return null;
    }

    private IType? TryGetRefinedTypeFromLogicalOr(LogicalOrNode logicalOr)
    {
        if(logicalOr.Right is not null)
        {
            return null;
        }

        var logicalAnd = logicalOr.Left;
        if(logicalAnd.Right is not null)
        {
            return null;
        }

        var equality = logicalAnd.Left;
        if(equality.Right is not null)
        {
            return null;
        }

        var relational = equality.Left;
        if(relational is not RelationalComparisonRelationalNode cmp)
        {
            return null;
        }

        if(cmp.Right is not null)
        {
            return null;
        }

        var additive = cmp.Left;
        if(additive.Right is not null)
        {
            return null;
        }

        var multiplicative = additive.Left;
        if(multiplicative.Right is not null)
        {
            return null;
        }

        var unary = multiplicative.Left;
        if(unary is not PostfixExpressionUnaryNode postfixExpr)
        {
            return null;
        }

        var postfix = postfixExpr.Primary;

        // For a simple identifier with no suffixes, check the target's symbol.
        if(postfix.Suffix.Length == 0)
        {
            var targetSymbol = GetDeclaredSymbol(postfix.Target);
            if(targetSymbol is not null
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
            if(expressionTypes.TryGetValue(postfix, out var postfixType)
                && postfixType is not TypeParameterType)
            {
                return postfixType;
            }
        }

        return null;
    }

    public void SetTypeOf(Storm.Runtime.SyntaxNode node, IType type)
    {
        expressionTypes[node] = type;
    }

    public void RecordResolvedOperator(Storm.Runtime.SyntaxNode node, ISymbol op)
    {
        resolvedOperators[node] = op;
    }

    public ISymbol? GetResolvedOperator(Storm.Runtime.SyntaxNode node)
    {
        return resolvedOperators.TryGetValue(node, out var op) ? op : null;
    }

    public RefinementPredicate? GetParameterRefinement(ISymbol parameterSymbol)
    {
        return parameterRefinements.TryGetValue(parameterSymbol, out var predicate) ? predicate : null;
    }

    public void RecordGuardNeeded(ISymbol parameterSymbol)
    {
        _ = guardNeededParameters.Add(parameterSymbol);
    }

    public bool IsGuardElidable(ISymbol parameterSymbol)
    {
        // A guard is elidable if the parameter has a refinement constraint but no call site
        // required a runtime guard (all call sites passed validated constants or arguments
        // with provably-satisfying refinement types).
        return parameterRefinements.ContainsKey(parameterSymbol)
            && !guardNeededParameters.Contains(parameterSymbol);
    }

    /// <summary>
    /// Returns the list of discarded task expressions for the given block scope node,
    /// or an empty list if none were recorded. A discarded task is a bare async method
    /// call whose result is not assigned to a variable, awaited, or returned. The
    /// emitter uses this to capture those expressions into synthetic variables and
    /// emit scope-exit await code.
    /// </summary>
    public IReadOnlyList<DiscardedTaskInfo> GetDiscardedTasks(Storm.Runtime.SyntaxNode blockScopeNode)
    {
        return discardedTasksByScope.TryGetValue(blockScopeNode, out var tasks)
            ? tasks
            : Array.Empty<DiscardedTaskInfo>();
    }

    /// <summary>
    /// Returns all block scope nodes that have discarded tasks recorded.
    /// </summary>
    internal IReadOnlyDictionary<Storm.Runtime.SyntaxNode, List<DiscardedTaskInfo>> DiscardedTasksByScope => discardedTasksByScope;

    /// <inheritdoc />
    public void RecordInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode, IReadOnlyList<IType> inferredTypeArgs)
    {
        inferredTypeArguments[callSiteNode] = inferredTypeArgs;
    }

    /// <inheritdoc />
    public IReadOnlyList<IType>? GetInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode)
    {
        return inferredTypeArguments.TryGetValue(callSiteNode, out var args) ? args : null;
    }

    /// <inheritdoc />
    public void RecordImplicitClone(Storm.Runtime.SyntaxNode source, IType destinationType)
    {
        implicitCloneSites[source] = destinationType;
    }

    /// <inheritdoc />
    public IType? GetImplicitCloneDestination(Storm.Runtime.SyntaxNode source)
    {
        return implicitCloneSites.TryGetValue(source, out var destinationType) ? destinationType : null;
    }

    /// <inheritdoc />
    public void RecordAsConversionKind(Storm.Runtime.SyntaxNode node, AsConversionKind kind)
    {
        asConversionKinds[node] = kind;
    }

    /// <inheritdoc />
    public AsConversionKind? GetAsConversionKind(Storm.Runtime.SyntaxNode node)
    {
        return asConversionKinds.TryGetValue(node, out var kind) ? kind : null;
    }
}