using System.Collections.Immutable;
using Mist.Driver.Contracts;
using Mist.Semantics.Model;

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
    /// The reachability map produced by the <c>unknown-type</c> feature's
    /// Phase-C Reachability Analyzer. For every member of every generic
    /// type declaration, this map records whether invoking that member
    /// could write to storage whose declared type transitively contains a
    /// given type parameter. <see cref="Mist.Analysis.Builtin.TypeCheckerAnalyzer"/>
    /// consults the map during member-access resolution when the receiver's
    /// static type is a closed generic with an <c>unknown</c> type argument,
    /// filtering out members that would write through the <c>unknown</c>-typed
    /// slot (Requirement 10.1–10.4).
    /// </summary>
    /// <remarks>
    /// Always non-null. For semantic models built without running the
    /// analyzer (e.g., minimal in-memory models used by tests), the map
    /// is <see cref="Mist.Semantics.Model.ReachabilityAnalyzer.Empty"/>.
    /// </remarks>
    IReachabilityMap ReachabilityMap { get; }
}
