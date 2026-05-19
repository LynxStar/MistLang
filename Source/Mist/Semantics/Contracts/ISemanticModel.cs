using System.Collections.Immutable;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Contracts;

/// <summary>
/// An immutable representation of Mist source code enriched with resolved symbols,
/// types, scopes, and other semantic information.
/// </summary>
public interface ISemanticModel
{
    MistCompilation Compilation { get; }
    IScope GlobalScope { get; }
    IReadOnlyList<CompilationUnit> Units { get; }

    /// <summary>
    /// The AST roots corresponding to each compilation unit, in the same order as <see cref="Units"/>.
    /// </summary>
    ImmutableArray<Storm.Runtime.SyntaxNode> Asts { get; }

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

    /// <summary>
    /// Sets the type of the given AST node expression. Used by analyzers that
    /// compute types for expressions after the semantic model is constructed
    /// (e.g., operator overload resolution in <c>TypeCheckerAnalyzer</c>
    /// resolves <c>a + b</c> to a user-defined operator and records the result
    /// type on the enclosing expression node).
    /// </summary>
    void SetTypeOf(Storm.Runtime.SyntaxNode node, IType type);

    /// <summary>
    /// Records the operator symbol selected by overload resolution for the given
    /// operator expression node. Called by the type checker after it resolves an
    /// operator call (e.g., <c>a + b</c>) so that the emitter can later retrieve
    /// which operator was chosen via <see cref="GetResolvedOperator"/>.
    /// </summary>
    void RecordResolvedOperator(Storm.Runtime.SyntaxNode node, ISymbol op);

    /// <summary>
    /// Returns the operator symbol previously recorded for the given AST node by
    /// <see cref="RecordResolvedOperator"/>, or <c>null</c> if no operator has
    /// been recorded for that node.
    /// </summary>
    ISymbol? GetResolvedOperator(Storm.Runtime.SyntaxNode node);

    ImmutableArray<ISymbol> AllSymbols { get; }

    /// <summary>
    /// The trait satisfaction map recording which types satisfy which traits
    /// (structurally or via <c>with</c> blocks). Populated during
    /// <c>Pass1_ResolveTraitSatisfaction</c>.
    /// </summary>
    TraitSatisfactionMap TraitSatisfactionMap { get; }



    /// <summary>
    /// The extension registry indexing all extension blocks by receiver type.
    /// Populated by the <see cref="Mist.Semantics.Model.SemanticModelBuilder"/>
    /// and consumed by the type checker during member lookup fallback.
    /// </summary>
    ExtensionRegistry ExtensionRegistry { get; }

    /// <summary>
    /// Returns the adhoc refinement predicate for a parameter symbol, or null if none.
    /// </summary>
    RefinementPredicate? GetParameterRefinement(ISymbol parameterSymbol);

    /// <summary>
    /// Records that a parameter with a refinement constraint has at least one call site
    /// that cannot be statically validated (non-constant, non-refinement-typed argument),
    /// meaning its runtime guard cannot be elided.
    /// </summary>
    void RecordGuardNeeded(ISymbol parameterSymbol);

    /// <summary>
    /// Returns true if the parameter has a refinement constraint and all call sites pass
    /// only validated constants or arguments with provably-satisfying refinement types,
    /// meaning the runtime guard can be safely elided.
    /// </summary>
    bool IsGuardElidable(ISymbol parameterSymbol);

    /// <summary>
    /// Returns the list of live (un-awaited, un-returned) tasks for the given
    /// block scope node, or an empty list if no live tasks were recorded.
    /// The emitter uses this to generate scope-exit await code.
    /// </summary>
    IReadOnlyList<LiveTaskInfo> GetLiveTasks(Storm.Runtime.SyntaxNode blockScopeNode);
}
