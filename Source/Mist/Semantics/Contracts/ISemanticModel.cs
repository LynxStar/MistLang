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
    /// The extension registry indexing all extension blocks by receiver type.
    /// Populated by the <see cref="Mist.Semantics.Model.SemanticModelBuilder"/>
    /// and consumed by the type checker during member lookup fallback.
    /// </summary>
    ExtensionRegistry ExtensionRegistry { get; }

    /// <summary>
    /// Records with-block conformance registrations established via
    /// <c>with Type : AbstractType { mappings }</c> declarations.
    /// Populated by the <see cref="Mist.Semantics.Model.SemanticModelBuilder"/>
    /// and consumed by the type checker for assignability checks and by the
    /// emitter for explicit interface implementation generation.
    /// </summary>
    ConformanceMap ConformanceMap { get; }

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
    /// Returns the list of discarded task expressions for the given block scope node,
    /// or an empty list if none were recorded. A discarded task is a bare async method
    /// call whose result is not assigned to a variable, awaited, or returned. The
    /// emitter uses this to capture those expressions into synthetic variables and
    /// emit scope-exit await code.
    /// </summary>
    IReadOnlyList<DiscardedTaskInfo> GetDiscardedTasks(Storm.Runtime.SyntaxNode blockScopeNode);

    /// <summary>
    /// Records the inferred type arguments for a method call node where type
    /// arguments were omitted in source but successfully inferred.
    /// </summary>
    void RecordInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode, IReadOnlyList<IType> inferredTypeArgs);

    /// <summary>
    /// Returns the inferred type arguments for a method call node, or <c>null</c> if
    /// type arguments were not inferred (either explicit or inference not attempted).
    /// </summary>
    IReadOnlyList<IType>? GetInferredTypeArguments(Storm.Runtime.SyntaxNode callSiteNode);

    /// <summary>
    /// Records that an implicit <c>.Clone()</c> must be inserted at the given source
    /// expression because it is a non-owned source bridged into an owned destination via
    /// <c>IClonable&lt;T&gt;</c> (Ownership v2, Req 6 AC 9, Req 8, Req 14 AC 7).
    /// The <c>MoveBoundaryResolver</c> records these annotations during ownership analysis;
    /// the C# emitter (task 12.1) reads them to emit the <c>.Clone()</c> call (or elide it
    /// for pure-struct value types). <paramref name="destinationType"/> is the owned
    /// destination's type, retained so the emitter can apply the pure-struct optimization.
    /// </summary>
    void RecordImplicitClone(Storm.Runtime.SyntaxNode source, IType destinationType);

    /// <summary>
    /// Returns the owned destination type recorded for an implicit-clone site by
    /// <see cref="RecordImplicitClone"/>, or <c>null</c> if the given source expression was
    /// not marked as an implicit-clone site.
    /// </summary>
    IType? GetImplicitCloneDestination(Storm.Runtime.SyntaxNode source);

    /// <summary>
    /// Records the conversion classification for an <c>as</c> expression node. Called by
    /// the type checker after it determines whether the conversion is provable (identity,
    /// widening) or non-provable (narrowing, union extraction, unknown narrowing). The
    /// emitter reads this via <see cref="GetAsConversionKind"/> to decide whether to emit
    /// a direct conversion or a runtime check.
    /// </summary>
    void RecordAsConversionKind(Storm.Runtime.SyntaxNode node, AsConversionKind kind);

    /// <summary>
    /// Returns the conversion classification previously recorded for the given <c>as</c>
    /// expression node by <see cref="RecordAsConversionKind"/>, or <c>null</c> if no
    /// classification has been recorded for that node.
    /// </summary>
    AsConversionKind? GetAsConversionKind(Storm.Runtime.SyntaxNode node);
}