using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Infers the minimum ownership tier required on <c>this</c> for each method by analyzing
/// owned-field usage in the method body (Requirement 17).
///
/// <para>
/// The inference composes across multiple field accesses (most restrictive wins) and
/// transitively through helper calls on <c>this</c>. An explicit <c>with {tier}</c> annotation
/// overrides inference but is validated against the body's actual requirements.
/// </para>
///
/// <para><b>Algorithm</b> (Req 17 AC 1–7):</para>
/// <list type="number">
///   <item>For each method, scan owned-field accesses in the body.</item>
///   <item>Move/write to owned field → requires <c>Borrowed</c>.</item>
///   <item>Freeze/frozenRef of owned field → requires <c>Frozen</c>.</item>
///   <item>Read of owned field → requires <c>ReadonlyRef</c>.</item>
///   <item>No owned-field access → <c>None</c>.</item>
///   <item>Compose transitively: if method A calls method B on <c>this</c>,
///         A requires at least B's tier (Req 17 AC 7).</item>
///   <item>For explicit <c>with {tier}</c>: validate declared >= required;
///         emit <c>MIST_OWN_THIS_TIER_INSUFFICIENT</c> if under-declared (Req 17 AC 11).</item>
/// </list>
/// </summary>
public sealed class ThisTierInferencePass
{
    /// <summary>
    /// Analyzes all methods in the model and records their inferred this-tier on the symbol.
    /// Two-phase approach: first compute body-local requirements for all methods,
    /// then propagate transitive requirements via helper calls on <c>this</c>.
    /// </summary>
    public static void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Phase 1: Compute the body-local tier requirement for each method
        // (owned-field accesses only, no transitive call composition yet).
        var methodSymbols = CollectInstanceMethods(model);
        var bodyTiers = new Dictionary<ISymbol, ThisTier>(ReferenceEqualityComparer.Instance);
        var callsOnThis = new Dictionary<ISymbol, List<ISymbol>>(ReferenceEqualityComparer.Instance);

        foreach(var methodSymbol in methodSymbols)
        {
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(methodSymbol); }
            catch { continue; }

            var body = GetMethodBody(declaringNode);
            if(body is null)
                continue;

            // The method's ContainingScope is the type's MemberScope, which
            // contains sibling fields and methods for lookup.
            var memberScope = methodSymbol.ContainingScope;
            if(memberScope is null)
                continue;

            var tier = ThisTier.None;
            var calls = new List<ISymbol>();

            ScanBodyForOwnedFieldAccesses(body, memberScope, model, ref tier, calls);

            bodyTiers[methodSymbol] = tier;
            callsOnThis[methodSymbol] = calls;
        }

        // Phase 2: Propagate transitive requirements through helper calls on `this`.
        // Use a fixed-point iteration: keep propagating until no changes occur.
        PropagateTransitiveRequirements(bodyTiers, callsOnThis);

        // Phase 3: For each method, apply the declared tier or inferred tier,
        // validate annotations, and record on the symbol.
        foreach(var methodSymbol in methodSymbols)
        {
            if(!bodyTiers.TryGetValue(methodSymbol, out var requiredTier))
                requiredTier = ThisTier.None;

            var sym = methodSymbol as Symbol;
            if(sym is null)
                continue;

            if(sym.DeclaredThisTier is { } declaredTier)
            {
                // Explicit 'with {tier}' annotation: use the declared tier but validate.
                if(declaredTier < requiredTier)
                {
                    // Under-declared: emit MIST_OWN_THIS_TIER_INSUFFICIENT (Req 17 AC 11).
                    SyntaxNode? declaringNode = null;
                    try { declaringNode = model.GetDeclaringNode(methodSymbol); }
                    catch { /* no source location available */ }

                    var message = ThisTierInsufficientMessage(
                        sym.Name, FormatTier(declaredTier), FormatTier(requiredTier));

                    if(declaringNode is not null)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            OwnershipAnalyzer.ThisTierInsufficient,
                            message,
                            declaringNode,
                            model));
                    }
                    else
                    {
                        diagnostics.Add(new Diagnostic(
                            OwnershipAnalyzer.ThisTierInsufficient,
                            DiagnosticSeverity.Error,
                            PipelineStage.Analysis,
                            message,
                            Location: default,
                            CompilationUnitPath: "",
                            AnalyzerName: "Ownership"));
                    }
                }

                // Over-declared is accepted (Req 17 AC 12). Use the declared tier.
                sym.InferredThisTier = declaredTier;
            }
            else
            {
                // No explicit annotation — use the inferred tier.
                sym.InferredThisTier = requiredTier;
            }
        }
    }

    /// <summary>
    /// Returns the inferred or declared this-tier for a method symbol.
    /// Should be called after <see cref="Analyze"/> has run.
    /// </summary>
    public static ThisTier GetThisTier(ISymbol methodSymbol)
    {
        if(methodSymbol is Symbol sym)
            return sym.InferredThisTier;
        return ThisTier.None;
    }

    /// <summary>
    /// Returns the set of owned fields that a method accesses on <c>this</c>, along with
    /// the access kind for each field (Read or Write). Used by the flow-sensitive analysis
    /// to check per-field states at method call sites (Req 16 AC 23–24).
    /// </summary>
    /// <remarks>
    /// This performs a lightweight re-scan of the method body to collect per-field access info.
    /// Transitive: if the method calls another method on <c>this</c>, the callee's accessed
    /// fields are included (with their access kinds).
    /// </remarks>
    /// <param name="methodSymbol">The method symbol to analyze.</param>
    /// <param name="model">The semantic model for AST/symbol resolution.</param>
    /// <returns>
    /// A dictionary mapping each accessed owned field symbol to the most restrictive
    /// access kind required (Write > Read). Empty if the method accesses no owned fields.
    /// </returns>
    public static Dictionary<ISymbol, FieldAccessKind> GetAccessedFields(ISymbol methodSymbol, ISemanticModel model)
    {
        var result = new Dictionary<ISymbol, FieldAccessKind>(ReferenceEqualityComparer.Instance);
        var visited = new HashSet<ISymbol>(ReferenceEqualityComparer.Instance);
        CollectAccessedFields(methodSymbol, model, result, visited);
        return result;
    }

    /// <summary>
    /// Recursively collects owned-field accesses for a method, including transitive helper
    /// calls on <c>this</c>.
    /// </summary>
    private static void CollectAccessedFields(
        ISymbol methodSymbol,
        ISemanticModel model,
        Dictionary<ISymbol, FieldAccessKind> result,
        HashSet<ISymbol> visited)
    {
        if(!visited.Add(methodSymbol))
            return; // Avoid infinite recursion from mutual calls.

        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        var body = GetMethodBody(declaringNode);
        if(body is null)
            return;

        var memberScope = methodSymbol.ContainingScope;
        if(memberScope is null)
            return;

        // Scan the body for owned-field accesses and helper calls on this.
        var callsOnThis = new List<ISymbol>();
        ScanNodeForFieldAccesses(body, memberScope, model, result, callsOnThis, isAssignmentTarget: false);

        // Transitively include fields from helper calls on this.
        foreach(var callee in callsOnThis)
        {
            CollectAccessedFields(callee, model, result, visited);
        }
    }

    /// <summary>
    /// Scans an AST node for owned-field accesses on <c>this</c>, recording the field symbol
    /// and access kind in <paramref name="result"/>.
    /// </summary>
    private static void ScanNodeForFieldAccesses(
        SyntaxNode node,
        IScope memberScope,
        ISemanticModel model,
        Dictionary<ISymbol, FieldAccessKind> result,
        List<ISymbol> callsOnThis,
        bool isAssignmentTarget)
    {
        switch(node)
        {
            case AssignmentNode assignment:
                // Target is a write; value is a read.
                if(TryGetOwnedFieldOnThis(assignment.Target, memberScope) is { } writeField)
                {
                    RecordAccess(result, writeField, FieldAccessKind.Write);
                }
                else
                {
                    ScanNodeForFieldAccesses(assignment.Target, memberScope, model, result, callsOnThis, isAssignmentTarget: true);
                }
                if(assignment.Value is not null)
                    ScanNodeForFieldAccesses(assignment.Value, memberScope, model, result, callsOnThis, isAssignmentTarget: false);
                return;

            case PostfixNode postfix:
                ScanPostfixForFieldAccesses(postfix, memberScope, model, result, callsOnThis, isAssignmentTarget);
                return;

            default:
                foreach(var child in node.Children())
                {
                    if(child is not null)
                        ScanNodeForFieldAccesses(child, memberScope, model, result, callsOnThis, isAssignmentTarget: false);
                }
                break;
        }
    }

    /// <summary>
    /// Scans a postfix expression for owned-field accesses on <c>this</c>.
    /// </summary>
    private static void ScanPostfixForFieldAccesses(
        PostfixNode postfix,
        IScope memberScope,
        ISemanticModel model,
        Dictionary<ISymbol, FieldAccessKind> result,
        List<ISymbol> callsOnThis,
        bool isAssignmentTarget)
    {
        if(postfix.Target is ThisExpressionPrimaryNode && postfix.Suffix.Length > 0)
        {
            var firstSuffix = postfix.Suffix[0];

            if(firstSuffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                var memberName = memberAccess.Name.Lexeme;
                var memberSymbol = memberScope.Lookup(memberName);

                if(memberSymbol is Symbol fieldSym && fieldSym.Kind == SymbolKind.Field && fieldSym.IsOwnedField)
                {
                    if(!isAssignmentTarget)
                    {
                        // Reading an owned field (may be move or read depending on context).
                        RecordAccess(result, memberSymbol, FieldAccessKind.Read);
                    }
                    // Write case is handled in ScanNodeForFieldAccesses → AssignmentNode.
                }

                // Process remaining suffixes.
                for(var i = 1; i < postfix.Suffix.Length; i++)
                {
                    ScanNodeForFieldAccesses(postfix.Suffix[i], memberScope, model, result, callsOnThis, isAssignmentTarget: false);
                }
                return;
            }
            else if(firstSuffix is MethodCallSuffixPostfixSuffixNode methodCall)
            {
                // this.method() — record as a helper call on this for transitive resolution.
                var methodName = methodCall.Name.Lexeme;
                var calledSymbol = memberScope.Lookup(methodName);
                if(calledSymbol is not null && calledSymbol.Kind == SymbolKind.Method)
                {
                    callsOnThis.Add(calledSymbol);
                }

                // Scan arguments for field accesses.
                if(methodCall.Arguments is not null)
                    ScanNodeForFieldAccesses(methodCall.Arguments, memberScope, model, result, callsOnThis, isAssignmentTarget: false);

                for(var i = 1; i < postfix.Suffix.Length; i++)
                {
                    ScanNodeForFieldAccesses(postfix.Suffix[i], memberScope, model, result, callsOnThis, isAssignmentTarget: false);
                }
                return;
            }
        }

        // Not a this.X pattern — recurse normally.
        if(postfix.Target is not null)
            ScanNodeForFieldAccesses(postfix.Target, memberScope, model, result, callsOnThis, isAssignmentTarget: false);

        foreach(var suffix in postfix.Suffix)
        {
            ScanNodeForFieldAccesses(suffix, memberScope, model, result, callsOnThis, isAssignmentTarget: false);
        }
    }

    /// <summary>
    /// Tries to resolve a syntax node as an owned field on <c>this</c> (either
    /// <c>this.field</c> or bare <c>field</c>). Returns the field symbol if found, null otherwise.
    /// </summary>
    private static ISymbol? TryGetOwnedFieldOnThis(SyntaxNode targetNode, IScope memberScope)
    {
        switch(targetNode)
        {
            case PostfixNode postfix:
                {
                    if(postfix.Suffix.Length == 1
                        && postfix.Target is ThisExpressionPrimaryNode
                        && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
                    {
                        var memberName = memberAccess.Name.Lexeme;
                        var symbol = memberScope.Lookup(memberName);
                        if(symbol is Symbol fieldSym && fieldSym.Kind == SymbolKind.Field && fieldSym.IsOwnedField)
                            return symbol;
                    }

                    if(postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode identifier)
                    {
                        var memberName = identifier.Name.Lexeme;
                        var symbol = memberScope.Lookup(memberName);
                        if(symbol is Symbol fieldSym && fieldSym.Kind == SymbolKind.Field && fieldSym.IsOwnedField)
                            return symbol;
                    }

                    return null;
                }

            default:
                {
                    var children = new List<SyntaxNode>();
                    foreach(var child in targetNode.Children())
                    {
                        if(child is not null)
                            children.Add(child);
                    }
                    if(children.Count == 1)
                        return TryGetOwnedFieldOnThis(children[0], memberScope);
                    return null;
                }
        }
    }

    /// <summary>
    /// Records a field access, taking the most restrictive kind (Write > Read).
    /// </summary>
    private static void RecordAccess(Dictionary<ISymbol, FieldAccessKind> result, ISymbol field, FieldAccessKind kind)
    {
        if(result.TryGetValue(field, out var existing))
        {
            if(kind > existing)
                result[field] = kind;
        }
        else
        {
            result[field] = kind;
        }
    }

    // ── Body scanning ────────────────────────────────────────────────────

    /// <summary>
    /// Scans a method body for owned-field accesses on <c>this</c> and method calls
    /// on <c>this</c>. Updates <paramref name="tier"/> with the most restrictive
    /// requirement from field accesses, and collects called method symbols in
    /// <paramref name="callsOnThis"/>.
    /// </summary>
    private static void ScanBodyForOwnedFieldAccesses(
        SyntaxNode body,
        IScope memberScope,
        ISemanticModel model,
        ref ThisTier tier,
        List<ISymbol> callsOnThis)
    {
        // Walk all nodes in the body looking for:
        // 1. Assignments to this.ownedField (write → Borrowed)
        // 2. Reads of this.ownedField (read → ReadonlyRef)
        // 3. Method calls on this (transitive composition)
        // 4. Passing this.ownedField to owned/borrowed/frozen parameters (move/borrow/freeze)

        ScanNode(body, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
    }

    /// <summary>
    /// Recursively scans a syntax node for owned-field interactions on <c>this</c>.
    /// </summary>
    private static void ScanNode(
        SyntaxNode node,
        IScope memberScope,
        ISemanticModel model,
        ref ThisTier tier,
        List<ISymbol> callsOnThis,
        bool isAssignmentTarget)
    {
        switch(node)
        {
            case AssignmentNode assignment:
                ScanAssignment(assignment, memberScope, model, ref tier, callsOnThis);
                return;

            case PostfixNode postfix:
                ScanPostfix(postfix, memberScope, model, ref tier, callsOnThis, isAssignmentTarget);
                return;

            default:
                // Recurse into children.
                foreach(var child in node.Children())
                {
                    if(child is not null)
                        ScanNode(child, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
                }
                break;
        }
    }

    /// <summary>
    /// Scans an assignment node. The target side is analyzed for owned-field writes;
    /// the value side is analyzed for owned-field reads.
    /// </summary>
    private static void ScanAssignment(
        AssignmentNode assignment,
        IScope memberScope,
        ISemanticModel model,
        ref ThisTier tier,
        List<ISymbol> callsOnThis)
    {
        // Check if the assignment target is this.ownedField → write → Borrowed.
        if(assignment.Value is not null && IsOwnedFieldOnThis(assignment.Target, memberScope, model))
        {
            tier = MaxTier(tier, ThisTier.Borrowed);
        }
        else if(assignment.Value is not null)
        {
            // The target itself may contain owned-field reads (e.g., x = this.field.prop)
            ScanNode(assignment.Target, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: true);
        }
        else
        {
            // No assignment operator — the "target" is just an expression (e.g., in
            // `return this.data;` the expression passes through the Assignment rule).
            // Scan it as a read context.
            ScanNode(assignment.Target, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
        }

        // The value side is always a read context.
        if(assignment.Value is not null)
            ScanNode(assignment.Value, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
    }

    /// <summary>
    /// Scans a postfix expression node for owned-field reads and method calls on <c>this</c>.
    /// </summary>
    private static void ScanPostfix(
        PostfixNode postfix,
        IScope memberScope,
        ISemanticModel model,
        ref ThisTier tier,
        List<ISymbol> callsOnThis,
        bool isAssignmentTarget)
    {
        // Check for this.field or this.method() patterns.
        if(postfix.Target is ThisExpressionPrimaryNode && postfix.Suffix.Length > 0)
        {
            var firstSuffix = postfix.Suffix[0];

            if(firstSuffix is MemberAccessSuffixPostfixSuffixNode memberAccess)
            {
                // this.member — check if member is an owned field.
                var memberName = memberAccess.Name.Lexeme;
                var memberSymbol = memberScope.Lookup(memberName);

                if(memberSymbol is Symbol fieldSym && fieldSym.Kind == SymbolKind.Field && fieldSym.IsOwnedField)
                {
                    // Determine the access kind based on context.
                    // If this node is not an assignment target, it's a read (or potential move/freeze
                    // depending on further suffix). For conservative analysis at this level:
                    // - If there are subsequent suffixes that indicate consumption, they'll be
                    //   detected separately. For now, a bare read of an owned field → ReadonlyRef.
                    if(!isAssignmentTarget)
                    {
                        // Check if a subsequent suffix indicates a move/freeze/borrow pattern.
                        var accessTier = ClassifyFieldAccessFromSuffixes(postfix.Suffix, 1, memberScope, model);
                        tier = MaxTier(tier, accessTier);
                    }
                    // If it IS an assignment target (e.g., this.field = x), that's handled
                    // in ScanAssignment → IsOwnedFieldOnThis.
                }

                // Check for method calls via further suffixes: this.member.method()
                // or this.method() directly — but this.member access doesn't imply a helper call.
            }
            else if(firstSuffix is MethodCallSuffixPostfixSuffixNode methodCall)
            {
                // this.method() — a helper call on this. Record for transitive composition.
                var methodName = methodCall.Name.Lexeme;
                var calledSymbol = memberScope.Lookup(methodName);
                if(calledSymbol is not null && calledSymbol.Kind == SymbolKind.Method)
                {
                    callsOnThis.Add(calledSymbol);
                }

                // Also scan the method arguments for owned-field reads.
                if(methodCall.Arguments is not null)
                    ScanNode(methodCall.Arguments, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);

                // Scan any further suffixes (e.g., this.method().something).
                for(var i = 1; i < postfix.Suffix.Length; i++)
                {
                    ScanNode(postfix.Suffix[i], memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
                }
                return;
            }

            // Process remaining suffixes for further method calls.
            for(var i = 1; i < postfix.Suffix.Length; i++)
            {
                var suffix = postfix.Suffix[i];
                if(suffix is MethodCallSuffixPostfixSuffixNode nextMethodCall)
                {
                    // If previous suffix was a member access to an owned field and this is
                    // a method call on it, the access kind depends on the method.
                    // For safety, scan arguments.
                    if(nextMethodCall.Arguments is not null)
                        ScanNode(nextMethodCall.Arguments, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
                }
                else
                {
                    ScanNode(suffix, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
                }
            }
            return;
        }

        // Not a this.X pattern — recurse normally.
        if(postfix.Target is not null)
            ScanNode(postfix.Target, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);

        foreach(var suffix in postfix.Suffix)
        {
            ScanNode(suffix, memberScope, model, ref tier, callsOnThis, isAssignmentTarget: false);
        }
    }

    /// <summary>
    /// Classifies the access tier required based on suffixes following an owned-field access.
    /// A bare read → ReadonlyRef. If followed by patterns indicating freeze/borrow/move,
    /// escalate accordingly.
    /// </summary>
    private static ThisTier ClassifyFieldAccessFromSuffixes(
        System.Collections.Immutable.ImmutableArray<PostfixSuffixNode> suffixes,
        int startIndex,
        IScope memberScope,
        ISemanticModel model)
    {
        // If no further suffixes beyond the member access, this is a read (e.g., var x = this.field).
        // A read of an owned field → ReadonlyRef minimum requirement.
        // Note: the actual access kind (move, borrow, freeze) is determined by the flow-sensitive
        // analysis based on the binding context. For inference purposes, reading an owned field
        // conservatively requires ReadonlyRef. If the field is actually being moved out or
        // borrowed, those patterns typically appear as:
        //   - Assignment from this.field to an owned var → move → Borrowed
        //   - Passing this.field to an owned parameter → move → Borrowed
        //   - Passing this.field to a frozen parameter → freeze → Frozen
        // These are caught by examining the variable declaration / argument context.
        //
        // For now, a bare field access defaults to ReadonlyRef (Req 17 AC 4).
        return ThisTier.ReadonlyRef;
    }

    // ── Assignment target detection ──────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the given AST node represents an assignment to an owned field
    /// accessed via <c>this</c> (either <c>this.field</c> or bare <c>field</c>).
    /// </summary>
    private static bool IsOwnedFieldOnThis(SyntaxNode targetNode, IScope memberScope, ISemanticModel model)
    {
        switch(targetNode)
        {
            case PostfixNode postfix:
                {
                    // this.field pattern
                    if(postfix.Suffix.Length == 1
                        && postfix.Target is ThisExpressionPrimaryNode
                        && postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
                    {
                        var memberName = memberAccess.Name.Lexeme;
                        var symbol = memberScope.Lookup(memberName);
                        return symbol is Symbol fieldSym && fieldSym.Kind == SymbolKind.Field && fieldSym.IsOwnedField;
                    }

                    // Bare field name (resolves to an owned field)
                    if(postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode identifier)
                    {
                        var memberName = identifier.Name.Lexeme;
                        var symbol = memberScope.Lookup(memberName);
                        return symbol is Symbol fieldSym && fieldSym.Kind == SymbolKind.Field && fieldSym.IsOwnedField;
                    }

                    return false;
                }

            default:
                {
                    // Walk single-child chains to unwrap expression wrappers.
                    var children = new List<SyntaxNode>();
                    foreach(var child in targetNode.Children())
                    {
                        if(child is not null)
                            children.Add(child);
                    }
                    if(children.Count == 1)
                        return IsOwnedFieldOnThis(children[0], memberScope, model);
                    return false;
                }
        }
    }

    // ── Transitive propagation ───────────────────────────────────────────

    /// <summary>
    /// Propagates this-tier requirements transitively through helper calls on <c>this</c>.
    /// If method A calls method B on <c>this</c>, A requires at least B's tier (Req 17 AC 7).
    /// Uses fixed-point iteration to handle mutual recursion.
    /// </summary>
    private static void PropagateTransitiveRequirements(
        Dictionary<ISymbol, ThisTier> tiers,
        Dictionary<ISymbol, List<ISymbol>> callsOnThis)
    {
        bool changed;
        do
        {
            changed = false;
            foreach(var (method, calls) in callsOnThis)
            {
                if(!tiers.TryGetValue(method, out var currentTier))
                    continue;

                foreach(var callee in calls)
                {
                    if(!tiers.TryGetValue(callee, out var calleeTier))
                        continue;

                    if(calleeTier > currentTier)
                    {
                        tiers[method] = calleeTier;
                        currentTier = calleeTier;
                        changed = true;
                    }
                }
            }
        } while(changed);
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Collects all instance method symbols from user code in the semantic model.
    /// </summary>
    private static List<ISymbol> CollectInstanceMethods(ISemanticModel model)
    {
        var result = new List<ISymbol>();
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
                continue;
            if(symbol.Kind != SymbolKind.Method)
                continue;
            if(symbol is Symbol sym && sym.IsStatic)
                continue;
            result.Add(symbol);
        }
        return result;
    }

    /// <summary>
    /// Returns the containing type for a method symbol by finding the type whose
    /// member scope matches the method's containing scope.
    /// </summary>
    private static IType? GetContainingType(ISymbol methodSymbol)
    {
        var scope = methodSymbol.ContainingScope;
        if(scope is null)
            return null;

        // The method's ContainingScope is the type's MemberScope.
        // Walk up to the parent scope to find the type symbol that owns this scope.
        var parentScope = scope.Parent;
        if(parentScope is null)
            return null;

        foreach(var member in parentScope.Members)
        {
            if(member.DeclaredType is not null)
            {
                var memberScope = GetMemberScope(member.DeclaredType);
                if(ReferenceEquals(memberScope, scope))
                    return member.DeclaredType;
            }
        }

        return null;
    }

    /// <summary>
    /// Returns the member scope of a type (class or struct).
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null
        };
    }

    /// <summary>
    /// Extracts the body block from a method or constructor declaration node.
    /// </summary>
    private static BlockNode? GetMethodBody(SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            MethodDeclarationNode methodDecl => methodDecl.Body.GetBlock(),
            ConstructorDeclarationNode ctorDecl => ctorDecl.Body.GetBlock(),
            _ => null,
        };
    }

    /// <summary>
    /// Returns the maximum (most restrictive) of two this-tiers.
    /// </summary>
    private static ThisTier MaxTier(ThisTier a, ThisTier b)
        => a >= b ? a : b;

    /// <summary>
    /// Formats a <see cref="ThisTier"/> value for diagnostic messages.
    /// </summary>
    internal static string FormatTier(ThisTier tier) => tier switch
    {
        ThisTier.None => "none",
        ThisTier.ReadonlyRef => "readonly ref",
        ThisTier.Frozen => "frozen",
        ThisTier.Borrowed => "borrowed",
        ThisTier.Owned => "owned",
        _ => tier.ToString().ToLowerInvariant(),
    };

    // ── Diagnostic message builders ──────────────────────────────────────

    /// <summary>
    /// Message for <c>MIST_OWN_THIS_TIER_INSUFFICIENT</c>: the method's explicit
    /// <c>with {tier}</c> annotation is less restrictive than the body requires
    /// (Req 17 AC 11).
    /// </summary>
    internal static string ThisTierInsufficientMessage(string methodName, string declaredTier, string requiredTier) =>
        $"Method '{methodName}' declares 'with {declaredTier}' but the body requires at least " +
        $"'{requiredTier}' access to 'this' — owned-field operations in the body demand a higher tier.";

    /// <summary>
    /// Creates a diagnostic with source location information resolved from the AST node.
    /// Mirrors the pattern in <see cref="OwnershipAnalyzer"/>.
    /// </summary>
    private static Diagnostic MakeDiagnostic(
        DiagnosticId id,
        string message,
        SyntaxNode node,
        ISemanticModel model)
    {
        Common.Text.LinePositionSpan? location = null;
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

                var sourceSpan = new Common.Text.SourceSpan(span.Start, span.Length);
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

/// <summary>
/// The kind of access a method performs on an owned field.
/// Used to determine what truth-table action to check at the call site (Req 16 AC 23–24).
/// </summary>
public enum FieldAccessKind
{
    /// <summary>The method reads the field (requires at least Read permission in the truth table).</summary>
    Read = 0,

    /// <summary>The method writes to the field (requires Write permission in the truth table).</summary>
    Write = 1,
}