using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// The kind of site requiring a move into an owned destination (Ownership v2 design,
/// Component 2). The site is informational for diagnostics; the resolution algorithm is
/// the same at every site (Req 6 AC 9 — a single move boundary).
/// </summary>
public enum OwnedMoveSite
{
    /// <summary><c>owned var x = &lt;source&gt;</c> (Req 1, Req 6).</summary>
    OwnedBindingInit,

    /// <summary><c>ownedInstance.Field = &lt;source&gt;</c> (Req 4).</summary>
    OwnedFieldAssignment,

    /// <summary><c>f(&lt;source&gt;)</c> where the parameter is <c>owned</c> (Req 6 AC 3, Req 9 AC 4).</summary>
    OwnedParameterPass,
}

/// <summary>
/// The outcome of resolving a move into an owned destination (Ownership v2 design,
/// Component 2).
/// </summary>
public enum MoveOutcome
{
    /// <summary>The source is already owned → consume it (mark the binding <c>Moved</c>).</summary>
    MovedFromOwned,

    /// <summary>
    /// The source is a value/primitive type → copy, no provenance needed
    /// (Req 4 AC 3, Req 4 AC 4).
    /// </summary>
    ValueCopy,

    /// <summary>
    /// The source is non-owned but implements <c>IClonable&lt;T&gt;</c> → insert
    /// <c>.Clone()</c> (Req 6 AC 9, Req 8).
    /// </summary>
    ImplicitClone,

    /// <summary>The source is <c>default(T)</c> / bare <c>default</c> → fresh graph (Req 6 AC 10).</summary>
    DefaultGraph,

    /// <summary>None of the above → a diagnostic has been emitted.</summary>
    Error,
}

/// <summary>
/// The result of resolving a move into an owned destination.
/// </summary>
/// <param name="Outcome">The classification of the move.</param>
/// <param name="ConsumedBinding">
/// The owned binding/parameter symbol that the move consumes, non-null only when
/// <paramref name="Outcome"/> is <see cref="MoveOutcome.MovedFromOwned"/> and the source was a
/// bare binding reference (a move). Null for constructor calls, owned-returning calls,
/// clones, value copies, defaults, and errors.
/// </param>
public readonly record struct MoveResolution(
    MoveOutcome Outcome,
    ISymbol? ConsumedBinding);

/// <summary>
/// Single authority for resolving moves into owned destinations (Requirement 6 AC 9).
/// Every owned-move site — owned binding initialization, field assignment on an
/// owned-bound instance, and owned-parameter passing — calls <see cref="Resolve"/>. This is
/// the ONLY place provenance and implicit-clone decisions are made in Ownership v2.
///
/// <para>
/// The resolver subsumes v1's <c>YieldsUniqueOwnership</c> / <c>IsConstructorOrOwnedCall</c>
/// provenance logic (formerly scattered in <c>OwnershipAnalyzer</c>) and centralizes it. The
/// resolution algorithm runs in a fixed order (design § "Resolution algorithm"):
/// </para>
/// <list type="number">
///   <item><c>default(T)</c> / bare <c>default</c> → <see cref="MoveOutcome.DefaultGraph"/> (Req 6 AC 10).</item>
///   <item>value/primitive type → <see cref="MoveOutcome.ValueCopy"/> (Req 4 AC 3/4).</item>
///   <item>already-owned source (constructor call, owned-returning call, live owned binding)
///         → <see cref="MoveOutcome.MovedFromOwned"/>, guarded by the truth table
///         (Req 6 AC 1–3, AC 8).</item>
///   <item>non-owned source implementing <c>IClonable&lt;T&gt;</c> →
///         <see cref="MoveOutcome.ImplicitClone"/> (Req 6 AC 9, Req 8 AC 2–5).</item>
///   <item>otherwise → <see cref="MoveOutcome.Error"/> emitting <c>MIST_OWN_REQUIRES_OWNED</c>
///         or <c>MIST_OWN_NOT_CLONABLE</c> (Req 8 AC 4, Req 13 AC 2/7).</item>
/// </list>
/// </summary>
public sealed class MoveBoundaryResolver
{
    /// <summary>
    /// The clonability oracle: given a source type, returns whether it implements
    /// <c>IClonable&lt;T&gt;</c>. The default reads the <c>ImplementsIClonable</c> flag cached
    /// on the type model by <see cref="IClonableDerivationEngine"/> (Req 8 AC 6–7). It is
    /// injectable so the resolver can be exercised in isolation without running the full
    /// derivation pass.
    /// </summary>
    private readonly Func<IType, bool> implementsIClonable;

    /// <summary>
    /// Creates a resolver that determines clonability from the cached
    /// <c>ImplementsIClonable</c> flag on the type model.
    /// </summary>
    public MoveBoundaryResolver()
        : this(DefaultImplementsIClonable)
    {
    }

    /// <summary>
    /// Creates a resolver with a custom clonability oracle. Primarily a unit-test seam that
    /// lets the resolver's provenance algorithm be checked without standing up the IClonable
    /// derivation pass.
    /// </summary>
    /// <param name="implementsIClonable">
    /// Predicate returning whether a source type implements <c>IClonable&lt;T&gt;</c>.
    /// </param>
    public MoveBoundaryResolver(Func<IType, bool> implementsIClonable)
    {
        ArgumentNullException.ThrowIfNull(implementsIClonable);
        this.implementsIClonable = implementsIClonable;
    }

    /// <summary>
    /// Resolves whether <paramref name="source"/> can move into an owned destination of
    /// <paramref name="destinationType"/> at the given <paramref name="site"/>.
    ///
    /// <para>
    /// Side effects:
    /// </para>
    /// <list type="bullet">
    ///   <item>For <see cref="MoveOutcome.ImplicitClone"/>, records a clone annotation on the
    ///         model via <see cref="ISemanticModel.RecordImplicitClone"/> so the emitter can
    ///         insert <c>.Clone()</c> (Req 14 AC 7).</item>
    ///   <item>For <see cref="MoveOutcome.MovedFromOwned"/> where the source is a bare binding
    ///         reference, marks that binding <see cref="VariableOwnershipState.Moved"/> in
    ///         <paramref name="flowState"/> (Req 6 AC 1–4). Cloning from a lent tier explicitly
    ///         does NOT transition the lender's state (Req 8 AC 5).</item>
    ///   <item>For <see cref="MoveOutcome.Error"/>, emits a diagnostic into
    ///         <paramref name="diagnostics"/>.</item>
    /// </list>
    /// </summary>
    /// <param name="source">The source expression being moved into the owned destination.</param>
    /// <param name="destinationType">The declared type of the owned destination.</param>
    /// <param name="site">The kind of owned-move site (for diagnostics).</param>
    /// <param name="flowState">The flow state for the enclosing method, used for move tracking and truth-table guards.</param>
    /// <param name="model">The semantic model.</param>
    /// <param name="diagnostics">The diagnostic bag to emit errors into.</param>
    /// <returns>The resolution describing how (or whether) the move is permitted.</returns>
    public MoveResolution Resolve(
        SyntaxNode source,
        IType? destinationType,

        MethodFlowState flowState,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(flowState);
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(diagnostics);

        // ── Step 1: default(T) / bare default → fresh owned graph (Req 6 AC 10) ──
        if(IsDefaultExpression(source))
        {
            return new MoveResolution(MoveOutcome.DefaultGraph, ConsumedBinding: null);
        }

        // ── Step 2: value/primitive types copy — never alias (Req 4 AC 3/4) ──
        var sourceType = model.GetTypeOf(source);
        if(sourceType is not null && (IsValueType(sourceType) || IsPrimitive(sourceType)))
        {
            return new MoveResolution(MoveOutcome.ValueCopy, ConsumedBinding: null);
        }

        // ── Step 3: already-owned source (Req 6 AC 1–3, AC 8) ──
        // A bare reference to an owned binding/parameter is a move from that binding.
        var referencedSymbol = TryResolveIdentifierSymbol(source, model);
        if(referencedSymbol is Symbol refSym &&
            refSym.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
        {
            if(refSym.OwnershipTier == OwnershipTier.Owned)
            {
                // The truth table guards the move: a move is only permitted from the
                // Live state. LentRef/LentFrozen/LentReadonly/Moved reject the move.
                if(flowState.VariableStates.TryGetValue(refSym, out var currentState) &&
                    !IsMovePermitted(currentState))
                {
                    ReportMoveStateViolation(refSym, currentState, source, model, diagnostics);
                    return new MoveResolution(MoveOutcome.Error, ConsumedBinding: null);
                }

                // Consume the owned binding: mark it Moved (Req 6 AC 1–4).
                flowState.VariableStates[refSym] = VariableOwnershipState.Moved;
                flowState.MoveLocations[refSym] =
                    new Common.Source.SourceSpan(source.Span.Start, source.Span.Length);

                return new MoveResolution(MoveOutcome.MovedFromOwned, ConsumedBinding: refSym);
            }

            // A non-owned binding never yields unique ownership by reference alone — it must
            // be bridged via IClonable (handled below).
        }
        else if(IsConstructorOrOwnedCall(source, model))
        {
            // Constructor call (new T()) or a call returning an owned value yields a fresh
            // uniquely-owned value with no binding to consume.
            return new MoveResolution(MoveOutcome.MovedFromOwned, ConsumedBinding: null);
        }

        // ── Step 4: non-owned source bridged via IClonable (Req 6 AC 9, Req 8 AC 2–5) ──
        // Cloning from a lent tier (borrowed/frozen/readonly ref) does NOT consume the
        // lender — Clone() reads through the reference (Req 8 AC 5). Because the clone
        // outcome never marks any binding Moved, the lender's flow state is left untouched.
        if(sourceType is not null && ImplementsIClonable(sourceType))
        {
            model.RecordImplicitClone(source, destinationType ?? sourceType);
            return new MoveResolution(MoveOutcome.ImplicitClone, ConsumedBinding: null);
        }

        // ── Step 5: nothing worked → error (Req 8 AC 4, Req 13 AC 2/7) ──
        ReportUnownedSource(source, sourceType, model, diagnostics);
        return new MoveResolution(MoveOutcome.Error, ConsumedBinding: null);
    }

    // ── Diagnostics ──────────────────────────────────────────────────────

    /// <summary>
    /// Reports the truth-table violation when a move is attempted from a binding that is not
    /// in the Live state (Req 6 AC 8). Reuses the existing ownership diagnostics so messages
    /// stay consistent with the analyzer's flow-sensitive reporting.
    /// </summary>
    private static void ReportMoveStateViolation(
        ISymbol variable,
        VariableOwnershipState state,
        SyntaxNode node,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var (id, message) = state switch
        {
            VariableOwnershipState.Moved =>
                (OwnershipAnalyzer.UseAfterMove,
                    $"Variable '{variable.Name}' has been moved and cannot be moved again here."),

            VariableOwnershipState.LentRef =>
                (OwnershipAnalyzer.ExclusiveViolation,
                    $"Cannot move '{variable.Name}' while a borrowed ref is outstanding. " +
                    $"The owner has no access until the ref is returned."),

            VariableOwnershipState.LentFrozen =>
                (OwnershipAnalyzer.MutationBlocked,
                    $"Cannot move '{variable.Name}' while a frozen ref is outstanding. " +
                    $"The new owner would start in Live state and could mutate, violating the frozen guarantee."),

            VariableOwnershipState.LentReadonly =>
                (OwnershipAnalyzer.ExclusiveViolation,
                    $"Cannot move '{variable.Name}' while a readonly ref is outstanding. " +
                    $"Moving would create a new owner with exclusive access, which is false while a readonly ref exists."),

            _ => (OwnershipAnalyzer.ExclusiveViolation,
                    $"Cannot move '{variable.Name}' in state '{state}'."),
        };

        diagnostics.Add(MakeDiagnostic(id, message, node, model));
    }

    /// <summary>
    /// Reports the error when a non-owned, non-clonable source is moved into an owned
    /// destination. Emits <c>MIST_OWN_NOT_CLONABLE</c> when the source is a known reference
    /// type that simply lacks <c>IClonable&lt;T&gt;</c> (Req 8 AC 4, Req 13 AC 7), otherwise
    /// the more general <c>MIST_OWN_REQUIRES_OWNED</c> (Req 1 AC 5, Req 4 AC 2, Req 13 AC 2).
    /// </summary>
    private static void ReportUnownedSource(
        SyntaxNode source,
        IType? sourceType,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // A resolvable reference type that does not implement IClonable gets the
        // clone-specific guidance.
        if(sourceType is not null && IsNamedReferenceType(sourceType))
        {
            diagnostics.Add(MakeDiagnostic(
                OwnershipAnalyzer.NotClonable,
                OwnershipAnalyzer.NotClonableMessage(sourceType.DisplayName),
                source,
                model));
            return;
        }

        var sourceText = GetNodeText(source, model);
        diagnostics.Add(MakeDiagnostic(
            OwnershipAnalyzer.RequiresOwned,
            OwnershipAnalyzer.RequiresOwnedMessage(sourceText, sourceText),
            source,
            model));
    }

    // ── Provenance helpers (absorbed from v1 OwnershipAnalyzer) ───────────

    /// <summary>
    /// Returns <c>true</c> when the expression is a <c>default(T)</c> expression or a bare
    /// <c>default</c> literal (Req 6 AC 10). Walks through single-child expression wrappers
    /// to find the underlying primary node.
    /// </summary>
    private static bool IsDefaultExpression(SyntaxNode node)
    {
        switch(node)
        {
            case DefaultExpressionPrimaryNode:
            case DefaultLiteralExpressionPrimaryNode:
                return true;

            case PostfixNode postfix when postfix.Suffix.Length == 0:
                return IsDefaultExpression(postfix.Target);
        }

        // Recurse through single non-null child wrappers.
        SyntaxNode? singleChild = null;
        var childCount = 0;
        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            childCount++;
            singleChild = child;
            if(childCount > 1)
            {
                break;
            }
        }

        return childCount == 1 && singleChild is not null && IsDefaultExpression(singleChild);
    }

    /// <summary>
    /// Checks whether an expression is a constructor call (<c>new T()</c>) or a function/method
    /// call that returns an owned value. Absorbed verbatim (behaviorally) from v1's
    /// <c>OwnershipAnalyzer.IsConstructorOrOwnedCall</c>.
    /// </summary>
    private static bool IsConstructorOrOwnedCall(SyntaxNode node, ISemanticModel model)
    {
        // Direct match on primary nodes.
        if(node is ObjectCreationExpressionPrimaryNode)
        {
            return true;
        }

        // PostfixNode with suffixes — check for a method call or invocation returning owned.
        if(node is PostfixNode postfix)
        {
            if(postfix.Suffix.Length > 0)
            {
                var lastSuffix = postfix.Suffix[^1];

                // Method call: receiver.Method()
                if(lastSuffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    var methodSymbol = model.GetDeclaredSymbol(methodCall);
                    if(methodSymbol is Symbol methodSym && methodSym.OwnershipTier == OwnershipTier.Owned)
                    {
                        return true;
                    }

                    if(methodSymbol is Symbol returnSym && returnSym.DeclaredType is not null && IsOwnedType(returnSym.DeclaredType))
                    {
                        return true;
                    }
                }

                // Direct invocation: func()
                if(lastSuffix is InvocationSuffixPostfixSuffixNode invocation)
                {
                    ISymbol? invokedSymbol = null;
                    if(postfix.Target is IdentifierExpressionPrimaryNode targetId)
                    {
                        invokedSymbol = model.GetDeclaredSymbol(targetId);
                    }

                    invokedSymbol ??= model.GetDeclaredSymbol(invocation);

                    if(invokedSymbol is Symbol funcSym && funcSym.OwnershipTier == OwnershipTier.Owned)
                    {
                        return true;
                    }

                    if(invokedSymbol is Symbol funcRetSym && funcRetSym.DeclaredType is not null && IsOwnedType(funcRetSym.DeclaredType))
                    {
                        return true;
                    }
                }

                return false;
            }

            // No suffixes — check if the target is a constructor call.
            if(postfix.Target is ObjectCreationExpressionPrimaryNode)
            {
                return true;
            }

            return false;
        }

        // Walk through single-child wrapper nodes (ExpressionNode → AssignmentNode → etc.)
        SyntaxNode? singleChild = null;
        var childCount = 0;
        foreach(var child in node.Children())
        {
            if(child is null)
            {
                continue;
            }

            childCount++;
            singleChild = child;
            if(childCount > 1)
            {
                break;
            }
        }

        return childCount == 1 && singleChild is not null && IsConstructorOrOwnedCall(singleChild, model);
    }

    /// <summary>
    /// Returns the symbol if the expression is a bare identifier referencing a local
    /// variable or parameter; null otherwise. Absorbed from v1's
    /// <c>OwnershipAnalyzer.TryResolveIdentifierSymbol</c>.
    /// </summary>
    private static ISymbol? TryResolveIdentifierSymbol(SyntaxNode node, ISemanticModel model)
    {
        if(node is ExpressionNode expr)
        {
            return expr.AsAssignmentOrNull() is { } exprAssignment
                ? TryResolveIdentifierSymbol(exprAssignment, model)
                : null;
        }

        if(node is AssignmentNode assignment)
        {
            if(assignment.Value is not null)
            {
                return null;
            }

            return TryResolveIdentifierSymbol(assignment.Target, model);
        }

        if(node is PostfixNode postfix)
        {
            if(postfix.Suffix.Length > 0)
            {
                return null;
            }

            return TryResolveIdentifierSymbol(postfix.Target, model);
        }

        if(node is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is not null &&
                symbol.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
            {
                return symbol;
            }

            return null;
        }

        var nonNullChildren = new List<SyntaxNode>();
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                nonNullChildren.Add(child);
            }
        }

        if(nonNullChildren.Count == 1)
        {
            return TryResolveIdentifierSymbol(nonNullChildren[0], model);
        }

        return null;
    }

    // ── Type classification helpers ───────────────────────────────────────

    /// <summary>Returns <c>true</c> if the given type is a value type (struct or enum).</summary>
    private static bool IsValueType(IType type)
    {
        var resolved = Unwrap(type);
        return resolved.Kind is TypeKind.Struct or TypeKind.Enum;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type is a primitive/bootstrap scalar type. Bootstrap
    /// value types are modeled as <see cref="StructType"/> (covered by <see cref="IsValueType"/>);
    /// <c>String</c> is modeled as a class and is treated here as a primitive for move purposes
    /// because it is immutable and copies do not introduce aliasing (Req 4 AC 4, Req 8 AC 6).
    /// </summary>
    private static bool IsPrimitive(IType type)
    {
        var resolved = Unwrap(type);
        if(resolved.Kind is TypeKind.Struct or TypeKind.Enum)
        {
            return true;
        }

        // String is the only reference-typed primitive.
        if(resolved is ClassType ct)
        {
            if(ct.IsBackendBacked && ct.ContainingNamespace == "Standard" && ct.DisplayName == "String")
            {
                return true;
            }

            if(ct.DisplayName == "string")
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type is declared as an owned type (owned class or
    /// struct). For constructed generic types the generic definition is consulted.
    /// </summary>
    private static bool IsOwnedType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false,
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the given type implements <c>IClonable&lt;T&gt;</c>, consulting
    /// the injected clonability oracle (Req 8 AC 6–7). Accessing the instance oracle is what
    /// makes <see cref="Resolve"/> a genuine instance method.
    /// </summary>
    private bool ImplementsIClonable(IType type)
    {
        return implementsIClonable(type);
    }

    /// <summary>
    /// The default clonability oracle: reads the <c>ImplementsIClonable</c> flag cached on the
    /// type model by <see cref="IClonableDerivationEngine"/>. For constructed generic types the
    /// generic definition is consulted.
    /// </summary>
    private static bool DefaultImplementsIClonable(IType type)
    {
        var resolved = Unwrap(type);
        resolved = resolved is ConstructedType constructed ? constructed.GenericDefinition : resolved;
        return resolved switch
        {
            ClassType ct => ct.ImplementsIClonable,
            StructType st => st.ImplementsIClonable,
            _ => false,
        };
    }

    /// <summary>
    /// Returns <c>true</c> for a named class/struct reference type whose name can be reported
    /// in a clone-specific diagnostic. Excludes value types (handled earlier) and unnamed
    /// constructs (errors, type parameters, abstract types, etc.).
    /// </summary>
    private static bool IsNamedReferenceType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved is ClassType;
    }

    /// <summary>
    /// Resolves transparent wrappers (type aliases, newtypes, refinement types) to the
    /// underlying nominal type. Mirrors <see cref="IClonableDerivationEngine"/>'s unwrapping
    /// so classification is consistent between derivation and resolution.
    /// </summary>
    private static IType Unwrap(IType type)
    {
        while(true)
        {
            switch(type)
            {
                case TypeAliasType alias:
                    type = alias.UnderlyingType;
                    break;
                case NewtypeType newtype:
                    type = newtype.SourceType;
                    break;
                case RefinementType refinement:
                    type = refinement.BaseType;
                    break;
                default:
                    return type;
            }
        }
    }

    /// <summary>
    /// Returns <c>true</c> when the truth table permits a move from the given state. Only the
    /// <see cref="VariableOwnershipState.Live"/> state allows a move (Req 6 AC 8).
    /// </summary>
    private static bool IsMovePermitted(VariableOwnershipState state)
    {
        return state == VariableOwnershipState.Live;
    }

    // ── Source / diagnostic plumbing ───────────────────────────────────────

    /// <summary>
    /// Extracts the source text covered by a node's span, for embedding in diagnostic
    /// messages. Returns a placeholder when the span cannot be resolved.
    /// </summary>
    private static string GetNodeText(SyntaxNode node, ISemanticModel model)
    {
        var span = node.Span;
        if(span.Length <= 0)
        {
            return "<expression>";
        }

        foreach(var unit in model.Units)
        {
            var source = unit.Source;
            if(source is null)
            {
                continue;
            }

            var contents = source.Contents;
            if(span.Start >= 0 && span.Start + span.Length <= contents.Length)
            {
                var text = contents.Substring(span.Start, span.Length).Trim();
                if(text.Length > 0)
                {
                    return text;
                }
            }
        }

        return "<expression>";
    }

    /// <summary>
    /// Creates an analysis diagnostic with source location resolved from the AST node span.
    /// Mirrors <c>OwnershipAnalyzer.MakeDiagnostic</c> so the resolver's diagnostics carry the
    /// same location and analyzer attribution.
    /// </summary>
    private static Diagnostic MakeDiagnostic(
        DiagnosticId id,
        string message,
        SyntaxNode node,
        ISemanticModel model)
    {
        Common.Source.LinePositionSpan? location = null;
        string? compilationUnitPath = null;

        var span = node.Span;
        if(span.Length > 0)
        {
            var asts = model.Asts;
            var units = model.Units;
            var astIndex = -1;

            for(var i = 0; i < asts.Length; i++)
            {
                var astSpan = asts[i].Span;
                if(span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
                {
                    astIndex = i;
                    break;
                }
            }

            if(astIndex >= 0 && astIndex < units.Count)
            {
                var source = units[astIndex].Source;
                compilationUnitPath = source.Path;

                var sourceSpan = new Common.Source.SourceSpan(span.Start, span.Length);
                location = source.ToLinePositionSpan(sourceSpan);
            }
        }

        return new Diagnostic(
            id,
            DiagnosticSeverity.Error,
            PipelineStage.Analysis,
            message,
            Location: location ?? default,
            CompilationUnitPath: compilationUnitPath ?? "",
            AnalyzerName: "Ownership");
    }
}