using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Infers ownership tiers for local 'var' bindings by finding the minimum tier
/// that satisfies all uses of each variable within the enclosing function (Requirement 18).
///
/// Uses a fixed-point algorithm over all var bindings in a function simultaneously,
/// trying tiers from most restrictive to least: owned → borrowed → frozen → readonly ref → shared.
/// Shared is always a valid fallback (Req 18 AC 6).
///
/// Does NOT apply to variables with explicit specifiers (owned var, borrowed var, etc.) —
/// those are validated directly by the flow-sensitive analysis.
/// </summary>
public sealed class OwnershipInferencePass
{
    /// <summary>
    /// Infers binding tiers for all var declarations in the given function body.
    /// Returns the inferred bindings indexed by symbol.
    /// </summary>
    public static IReadOnlyDictionary<ISymbol, InferredBinding> InferBindings(
        SyntaxNode functionBody,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var candidates = CollectVarDeclarations(functionBody, model);

        if(candidates.Count == 0)
        {
            return new Dictionary<ISymbol, InferredBinding>();
        }

        // Initialize candidates optimistically.
        InitializeCandidates(candidates, functionBody, model);

        // Fixed-point iteration: downgrade on conflicts until stable.
        RunFixedPoint(candidates, functionBody, model);

        // Build result — shared is the ultimate fallback (never conflicts).
        var result = new Dictionary<ISymbol, InferredBinding>(candidates.Count);
        foreach(var candidate in candidates)
        {
            // If we somehow ended up with no tier after exhausting downgrades,
            // emit MIST_OWN_INFERENCE_FAILURE and fall back to Shared.
            if(candidate.Tier == null)
            {
                diagnostics.Add(MakeDiagnostic(
                    OwnershipAnalyzer.InferenceFailure,
                    OwnershipAnalyzer.InferenceFailureMessage(
                        candidate.Symbol.Name,
                        FormatTier(candidate.LastConflictTier ?? OwnershipTier.Owned),
                        candidate.LastConflictUse ?? "a use",
                        FormatTier(candidate.LastConflictRequired ?? OwnershipTier.Unrestricted)),
                    candidate.DeclarationNode,
                    model));

                result[candidate.Symbol] = new InferredBinding(
                    candidate.Symbol,
                    OwnershipTier.Unrestricted,
                    InferenceReason.SharedFallback);
            }
            else
            {
                result[candidate.Symbol] = new InferredBinding(
                    candidate.Symbol,
                    candidate.Tier.Value,
                    candidate.Reason);
            }
        }

        return result;
    }

    // ── Collection ────────────────────────────────────────────────────────

    /// <summary>
    /// Collects all var declarations (no explicit ownership specifier) within the function body.
    /// These are the candidates for inference.
    /// </summary>
    private static List<InferenceCandidate> CollectVarDeclarations(SyntaxNode functionBody, ISemanticModel model)
    {
        var candidates = new List<InferenceCandidate>();
        CollectVarDeclarationsRecursive(functionBody, model, candidates);
        return candidates;
    }

    private static void CollectVarDeclarationsRecursive(SyntaxNode node, ISemanticModel model, List<InferenceCandidate> candidates)
    {
        // Check if this node is a variable declaration statement.
        if(node is VariableDeclarationStatementStatementNode varDeclStmt)
        {
            switch(varDeclStmt.VariableDeclaration)
            {
                case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                    {
                        // Only collect if no explicit ownership specifier.
                        if(inferred.OwnershipSpecifier is null)
                        {
                            var symbol = model.GetDeclaredSymbol(inferred);
                            if(symbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Unrestricted
                                && sym.Kind == SymbolKind.LocalVariable)
                            {
                                candidates.Add(new InferenceCandidate(sym, inferred, inferred.Initializer));
                            }
                        }

                        break;
                    }

                case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                    {
                        // Typed declarations with no ownership specifier (e.g., `T x = expr;`)
                        // are also candidates for inference.
                        if(typed.OwnershipSpecifier is null)
                        {
                            var symbol = model.GetDeclaredSymbol(typed);
                            if(symbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Unrestricted
                                && sym.Kind == SymbolKind.LocalVariable)
                            {
                                candidates.Add(new InferenceCandidate(sym, typed, typed.Initializer));
                            }
                        }

                        break;
                    }
            }
        }

        // Recurse into child nodes.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CollectVarDeclarationsRecursive(child, model, candidates);
            }
        }
    }

    // ── Initialization ────────────────────────────────────────────────────

    /// <summary>
    /// Initializes each candidate optimistically: if the initializer is a constructor call
    /// or an owned-returning call, start at Owned; otherwise start at Owned optimistically
    /// (will be downgraded on conflict).
    /// </summary>
    private static void InitializeCandidates(
        List<InferenceCandidate> candidates,
        SyntaxNode functionBody,
        ISemanticModel model)
    {
        foreach(var candidate in candidates)
        {
            if(candidate.Initializer is not null && IsConstructorOrOwnedCall(candidate.Initializer, model))
            {
                candidate.Tier = OwnershipTier.Owned;
                candidate.Reason = InferenceReason.OwnedInit;
            }
            else
            {
                // Start optimistic — owned, will downgrade if needed.
                candidate.Tier = OwnershipTier.Owned;
                candidate.Reason = InferenceReason.OwnedInit;
            }
        }
    }

    // ── Fixed-point iteration ─────────────────────────────────────────────

    /// <summary>
    /// Iterates over all candidates, checking each use site for conflicts with the current
    /// candidate tier. When a conflict is found, the candidate is downgraded. Iteration
    /// repeats until no changes occur (fixed-point reached).
    /// </summary>
    private static void RunFixedPoint(
        List<InferenceCandidate> candidates,
        SyntaxNode functionBody,
        ISemanticModel model)
    {
        // Pre-compute all uses for every candidate once. Uses are purely structural —
        // they don't change between iterations, only the inferred tier changes.
        // This eliminates repeated O(M) tree walks from the fixed-point loop.
        var usesCache = new Dictionary<ISymbol, List<VariableUse>>(
            candidates.Count, ReferenceEqualityComparer.Instance);
        foreach(var candidate in candidates)
        {
            usesCache[candidate.Symbol] = CollectUses(candidate.Symbol, functionBody, model);
        }

        // Early-out: candidates that have uses but ALL of them are Read will always
        // converge to Unrestricted. Remove them from the fixed-point loop entirely.
        // Candidates with zero uses stay in the loop — they keep their initial tier
        // (Owned for constructor-initialized, etc.) since nothing forces a downgrade.
        var activeCandidate = new List<InferenceCandidate>(candidates.Count);
        foreach(var candidate in candidates)
        {
            var uses = usesCache[candidate.Symbol];
            if(uses.Count > 0)
            {
                var hasNonReadUse = false;
                foreach(var use in uses)
                {
                    if(use.Kind != UseKind.Read)
                    {
                        hasNonReadUse = true;
                        break;
                    }
                }

                if(!hasNonReadUse)
                {
                    // All uses are reads — no ownership requirement. Skip the loop.
                    candidate.Tier = OwnershipTier.Unrestricted;
                    candidate.Reason = InferenceReason.Downgraded;
                    continue;
                }
            }

            activeCandidate.Add(candidate);
        }

        // Build a symbol → candidate lookup for cross-variable dependency resolution.
        var symbolToCandidateMap = new Dictionary<ISymbol, InferenceCandidate>(
            ReferenceEqualityComparer.Instance);
        foreach(var c in candidates)
        {
            symbolToCandidateMap[c.Symbol] = c;
        }

        // Maximum iterations to prevent infinite loops. With 5 possible tiers per candidate,
        // at most candidates.Count * 4 downgrades can occur before everything reaches Shared.
        var maxIterations = activeCandidate.Count * 5 + 1;
        for(var iteration = 0; iteration < maxIterations; iteration++)
        {
            var changed = false;

            foreach(var candidate in activeCandidate)
            {
                if(candidate.Tier is null || candidate.Tier == OwnershipTier.Unrestricted)
                {
                    continue; // Already at shared (ultimate fallback) — cannot conflict.
                }

                var uses = usesCache[candidate.Symbol];
                foreach(var use in uses)
                {
                    var requiredTier = DetermineRequiredTier(use, candidate.Symbol, symbolToCandidateMap, model);
                    if(requiredTier is null)
                    {
                        continue; // No ownership requirement for this use.
                    }

                    if(Conflicts(candidate.Tier.Value, requiredTier.Value))
                    {
                        var newTier = Downgrade(candidate.Tier.Value);
                        if(newTier != candidate.Tier)
                        {
                            candidate.LastConflictTier = candidate.Tier;
                            candidate.LastConflictRequired = requiredTier;
                            candidate.LastConflictUse = DescribeUse(use);
                            candidate.Tier = newTier;
                            candidate.Reason = InferenceReason.Downgraded;
                            changed = true;
                        }
                        else
                        {
                            // newTier == current tier means we've hit null (failure).
                            // This shouldn't happen because Shared never conflicts, but just in case:
                            candidate.Tier = null;
                            changed = true;
                        }

                        break; // Re-evaluate this candidate from scratch in the next iteration.
                    }
                }
            }

            if(!changed)
            {
                break; // Fixed-point reached.
            }
        }

        // Enforce minimum tier requirements from MethodReceiver uses.
        // The downgrade algorithm only handles "max tier" constraints (don't be too restrictive).
        // But MethodReceiver uses with a this-tier requirement impose a MINIMUM: the binding must
        // satisfy the method's this-tier. If the fixed-point downgraded below this minimum,
        // upgrade back to the minimum tier (Req 17 AC 15, Req 18 AC 1).
        foreach(var candidate in candidates)
        {
            if(candidate.Tier is null || candidate.Tier == OwnershipTier.Unrestricted)
            {
                // Reuse the pre-computed uses cache instead of re-walking the tree.
                var uses = usesCache[candidate.Symbol];
                var minimumTier = OwnershipTier.Unrestricted;
                foreach(var use in uses)
                {
                    if(use.Kind == UseKind.MethodReceiver)
                    {
                        // Try to resolve the method's this-tier either from the use's TargetMethod
                        // or by looking up the method on the candidate's declared type.
                        var effectiveTier = ThisTier.None;

                        if(use.TargetMethod is Symbol methodSym)
                        {
                            effectiveTier = methodSym.InferredThisTier;
                        }
                        else if(candidate.Symbol.DeclaredType is not null && use.Node is IdentifierExpressionPrimaryNode receiverNode)
                        {
                            // TargetMethod is null — try to find the method by walking the parent PostfixNode.
                            // The use was created from a PostfixNode where the identifier is the Target.
                            // Look at siblings to find the method name.
                            var candidateType = candidate.Symbol.DeclaredType;
                            if(candidateType is ClassType ct)
                            {
                                foreach(var member in ct.MemberScope.Members)
                                {
                                    if(member.Kind == SymbolKind.Method && member is Symbol ms && ms.InferredThisTier != ThisTier.None)
                                    {
                                        // Check if this method is actually called in this use context.
                                        // For safety, take the max of all methods with a tier requirement.
                                        if((int)ms.InferredThisTier > (int)effectiveTier)
                                        {
                                            effectiveTier = ms.InferredThisTier;
                                        }
                                    }
                                }
                            }
                        }

                        if(effectiveTier != ThisTier.None)
                        {
                            var requiredOwnershipTier = effectiveTier switch
                            {
                                ThisTier.ReadonlyRef => OwnershipTier.ReadonlyRef,
                                ThisTier.Frozen => OwnershipTier.Frozen,
                                ThisTier.Borrowed => OwnershipTier.Borrowed,
                                ThisTier.Owned => OwnershipTier.Owned,
                                _ => OwnershipTier.Unrestricted,
                            };
                            if((int)requiredOwnershipTier > (int)minimumTier)
                            {
                                minimumTier = requiredOwnershipTier;
                            }
                        }
                    }
                }

                if(minimumTier != OwnershipTier.Unrestricted)
                {
                    candidate.Tier = minimumTier;
                    candidate.Reason = InferenceReason.ThisTierRequired;
                }
            }
        }

        // Any candidate that didn't reach a stable tier falls back to Shared.
        foreach(var candidate in candidates)
        {
            if(candidate.Tier is null)
            {
                // This indicates a true inference failure (shouldn't normally happen).
                // Leave as null — the caller will emit MIST_OWN_INFERENCE_FAILURE.
            }
        }
    }

    // ── Conflict detection ────────────────────────────────────────────────

    /// <summary>
    /// Determines whether a candidate's current tier conflicts with the tier required by a use.
    /// A conflict occurs when the candidate tier is MORE restrictive than what the use needs.
    /// For example, Owned tier conflicts with a use that merely reads (which only needs Shared).
    /// 
    /// The logic is: if the candidate is at tier X but the use says "this variable should be
    /// at most Y" (where Y is less restrictive than X), then there's a conflict.
    /// </summary>
    private static bool Conflicts(OwnershipTier candidateTier, OwnershipTier requiredMaxTier)
    {
        // A conflict occurs when the candidate tier is MORE restrictive than allowed.
        // Higher numeric value = more restrictive in OwnershipTier enum.
        // RequiredMaxTier means "this use requires the binding to be at most this tier."
        // If candidateTier > requiredMaxTier, there's a conflict.
        return (int)candidateTier > (int)requiredMaxTier;
    }

    /// <summary>
    /// Downgrades a tier one step. The downgrade path is:
    /// Owned → Borrowed → Frozen → ReadonlyRef → Shared (Unrestricted).
    /// Returns null if already at Shared (should never happen because Shared never conflicts).
    /// </summary>
    private static OwnershipTier? Downgrade(OwnershipTier tier)
    {
        return tier switch
        {
            OwnershipTier.Owned => OwnershipTier.Borrowed,
            OwnershipTier.Borrowed => OwnershipTier.Frozen,
            OwnershipTier.Frozen => OwnershipTier.ReadonlyRef,
            OwnershipTier.ReadonlyRef => OwnershipTier.Unrestricted,
            OwnershipTier.Unrestricted => null, // Already at shared — should never conflict.
            _ => null,
        };
    }

    // ── Use collection ────────────────────────────────────────────────────

    /// <summary>
    /// Represents a single use of a variable in the function body.
    /// </summary>
    private sealed class VariableUse
    {
        public SyntaxNode Node { get; init; } = null!;
        public UseKind Kind { get; init; }
        public ISymbol? TargetMethod { get; init; }
    }

    private enum UseKind
    {
        /// <summary>Simple read (e.g., passing as non-owned argument, reading a field).</summary>
        Read,

        /// <summary>Used as receiver for a method call.</summary>
        MethodReceiver,

        /// <summary>Assigned to another variable (may consume ownership).</summary>
        AssignedToOther,

        /// <summary>Passed to an owned parameter (consumes the binding).</summary>
        OwnedParamConsumption,

        /// <summary>Passed as a borrowed parameter.</summary>
        BorrowedParam,

        /// <summary>Passed as a frozen parameter.</summary>
        FrozenParam,

        /// <summary>Passed as a readonly ref parameter.</summary>
        ReadonlyRefParam,
    }

    /// <summary>
    /// Collects all uses of the given symbol in the function body.
    /// Uses a context-passing approach since SyntaxNode has no Parent property.
    /// </summary>
    private static List<VariableUse> CollectUses(ISymbol symbol, SyntaxNode functionBody, ISemanticModel model)
    {
        var uses = new List<VariableUse>();
        CollectUsesRecursive(symbol, functionBody, model, uses, context: null);
        return uses;
    }

    /// <summary>
    /// Recursively walks the tree passing context about the enclosing node for use classification.
    /// </summary>
    private static void CollectUsesRecursive(
        ISymbol symbol,
        SyntaxNode node,
        ISemanticModel model,
        List<VariableUse> uses,
        UseContext? context)
    {
        // Check PostfixNode — if its Target references our symbol and has method call suffixes,
        // it's a method receiver use.
        if(node is PostfixNode postfix && postfix.Suffix.Length > 0)
        {
            if(postfix.Target is IdentifierExpressionPrimaryNode targetId)
            {
                var referencedSymbol = model.GetDeclaredSymbol(targetId);
                if(ReferenceEquals(referencedSymbol, symbol))
                {
                    var lastSuffix = postfix.Suffix[^1];
                    if(lastSuffix is MethodCallSuffixPostfixSuffixNode methodCall)
                    {
                        var methodSymbol = model.GetDeclaredSymbol(methodCall);
                        uses.Add(new VariableUse
                        {
                            Node = targetId,
                            Kind = UseKind.MethodReceiver,
                            TargetMethod = methodSymbol,
                        });
                    }
                    else
                    {
                        uses.Add(new VariableUse { Node = targetId, Kind = UseKind.Read });
                    }
                    // Don't recurse into the target again — we've handled it.
                    // Recurse into suffixes only.
                    foreach(var suffix in postfix.Suffix)
                    {
                        CollectUsesRecursive(symbol, suffix, model, uses, context);
                    }

                    return;
                }
            }
            // Fall through to standard recursion for other PostfixNode patterns.
        }

        // Check if this node is an identifier referencing our symbol.
        if(node is IdentifierExpressionPrimaryNode identifier)
        {
            var referencedSymbol = model.GetDeclaredSymbol(identifier);
            if(ReferenceEquals(referencedSymbol, symbol))
            {
                // Classify based on context passed in.
                var use = ClassifyUseFromContext(identifier, context, model);
                uses.Add(use);
                return; // Leaf node, no children to recurse.
            }
        }

        // For argument lists, pass argument context to children.
        if(node is ArgumentListNode argList)
        {
            // First argument.
            var firstArgContext = CreateArgumentContext(argList.Argument, model);
            CollectUsesRecursive(symbol, argList.Argument, model, uses, firstArgContext);

            // Additional arguments.
            foreach(var arg in argList.Arguments)
            {
                var argCtx = CreateArgumentContext(arg, model);
                CollectUsesRecursive(symbol, arg, model, uses, argCtx);
            }

            return;
        }

        // Recurse into children with the same context.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CollectUsesRecursive(symbol, child, model, uses, context);
            }
        }
    }

    /// <summary>
    /// Classifies a variable use based on the context it was found in.
    /// </summary>
    private static VariableUse ClassifyUseFromContext(
        IdentifierExpressionPrimaryNode identifier,
        UseContext? context,
        ISemanticModel model)
    {
        if(context is not null)
        {
            return context.Kind switch
            {
                UseKind.OwnedParamConsumption => new VariableUse
                { Node = identifier, Kind = UseKind.OwnedParamConsumption },
                UseKind.BorrowedParam => new VariableUse
                { Node = identifier, Kind = UseKind.BorrowedParam },
                UseKind.FrozenParam => new VariableUse
                { Node = identifier, Kind = UseKind.FrozenParam },
                UseKind.ReadonlyRefParam => new VariableUse
                { Node = identifier, Kind = UseKind.ReadonlyRefParam },
                _ => new VariableUse { Node = identifier, Kind = UseKind.Read },
            };
        }

        // Default: simple read.
        return new VariableUse { Node = identifier, Kind = UseKind.Read };
    }

    /// <summary>
    /// Creates a use context for an argument node based on the parameter's ownership tier.
    /// </summary>
    private static UseContext? CreateArgumentContext(SyntaxNode argNode, ISemanticModel model)
    {
        var paramSymbol = model.GetDeclaredSymbol(argNode);
        if(paramSymbol is Symbol param)
        {
            var kind = param.OwnershipTier switch
            {
                OwnershipTier.Owned => UseKind.OwnedParamConsumption,
                OwnershipTier.Borrowed => UseKind.BorrowedParam,
                OwnershipTier.Frozen => UseKind.FrozenParam,
                OwnershipTier.ReadonlyRef => UseKind.ReadonlyRefParam,
                _ => UseKind.Read,
            };
            return new UseContext { Kind = kind };
        }

        return null;
    }

    /// <summary>
    /// Carries context about the enclosing syntactic construct for use classification.
    /// </summary>
    private sealed class UseContext
    {
        public UseKind Kind { get; init; }
    }

    // ── Tier requirement determination ────────────────────────────────────

    /// <summary>
    /// Determines the maximum tier that a use permits. If the use requires the variable
    /// to be at a certain tier, it returns the tier that is the ceiling for the candidate.
    /// </summary>
    private static OwnershipTier? DetermineRequiredTier(
        VariableUse use,
        ISymbol candidateSymbol,
        Dictionary<ISymbol, InferenceCandidate> candidateMap,
        ISemanticModel model)
    {
        switch(use.Kind)
        {
            case UseKind.OwnedParamConsumption:
                // Passing to an owned parameter requires the binding to be owned.
                // This doesn't conflict — it REQUIRES Owned. Return Owned as the minimum.
                // Wait: the logic is inverted. The use requires the binding IS owned.
                // If the candidate isn't owned, we can't pass it as owned.
                // Actually, this means the binding MUST be at least owned.
                // Since owned is the highest, there's no conflict from above —
                // but if we've already downgraded below Owned, this creates a true conflict.
                // Return Owned as the required tier (the binding needs to be AT LEAST Owned).
                return null; // Can't be expressed as "max tier" — handled differently.

            case UseKind.BorrowedParam:
                // Passing to a borrowed parameter: the binding needs to be at least Borrowed.
                // If the binding is Owned, that's fine. If it's Frozen, that's a conflict
                // (can't lend as borrowed from a frozen binding).
                // This means we need at least Borrowed tier.
                return null; // Borrowing doesn't limit the tier from above.

            case UseKind.FrozenParam:
                // Passing to a frozen parameter: need at least Frozen tier.
                return null; // Freezing doesn't limit the tier from above.

            case UseKind.ReadonlyRefParam:
                // Passing to a readonly ref parameter: need at least ReadonlyRef.
                return null; // ReadonlyRef doesn't limit from above.

            case UseKind.MethodReceiver:
                // If the method requires a this-tier, the binding must satisfy it.
                // A method requiring a tier needs the binding to be at least that tier.
                // This doesn't limit from above — it sets a minimum.
                if(use.TargetMethod is Symbol methodSym)
                {
                    // Check declared or inferred this-tier on the method.
                    var effectiveTier = methodSym.InferredThisTier;
                    if(effectiveTier != ThisTier.None)
                    {
                        // The method requires a this-tier — the binding must satisfy it.
                        // This doesn't conflict with higher tiers, only with lower ones.
                        // Return null to indicate no UPPER limit, but record that a minimum is needed.
                        return null;
                    }
                }

                return null;

            case UseKind.AssignedToOther:
                // If the variable is assigned to another owned destination, it must be owned.
                return null;

            case UseKind.Read:
                // A simple read is compatible with any tier.
                // However, if the binding is Owned and used as a simple read (non-consuming),
                // it means ownership is not needed → the binding could be Shared.
                // This expresses that the max tier needed is Unrestricted (Shared).
                return OwnershipTier.Unrestricted;

            default:
                return null;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    /// <summary>
    /// Checks whether an expression is a constructor call (new T()) or a function/method
    /// call that returns an owned value.
    /// </summary>
    private static bool IsConstructorOrOwnedCall(SyntaxNode node, ISemanticModel model)
    {
        // Direct match on primary nodes.
        if(node is ObjectCreationExpressionPrimaryNode)
        {
            return true;
        }

        // PostfixNode with suffixes — check for a method call returning owned.
        if(node is PostfixNode postfix)
        {
            if(postfix.Suffix.Length > 0)
            {
                var lastSuffix = postfix.Suffix[^1];

                if(lastSuffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    var methodSymbol = model.GetDeclaredSymbol(methodCall);
                    if(methodSymbol is Symbol methodSym && methodSym.OwnershipTier == OwnershipTier.Owned)
                    {
                        return true;
                    }
                }

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
                }

                return false;
            }

            if(postfix.Target is ObjectCreationExpressionPrimaryNode)
            {
                return true;
            }

            return false;
        }

        // Walk through single-child wrapper nodes.
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

    private static string DescribeUse(VariableUse use)
    {
        return use.Kind switch
        {
            UseKind.Read => "a read",
            UseKind.MethodReceiver => "a method call",
            UseKind.AssignedToOther => "an assignment",
            UseKind.OwnedParamConsumption => "passing to an owned parameter",
            UseKind.BorrowedParam => "passing to a borrowed parameter",
            UseKind.FrozenParam => "passing to a frozen parameter",
            UseKind.ReadonlyRefParam => "passing to a readonly ref parameter",
            _ => "a use",
        };
    }

    private static string FormatTier(OwnershipTier tier)
    {
        return tier switch
        {
            OwnershipTier.Owned => "owned",
            OwnershipTier.Borrowed => "borrowed",
            OwnershipTier.Frozen => "frozen",
            OwnershipTier.ReadonlyRef => "readonly ref",
            OwnershipTier.Unrestricted => "shared",
            _ => "shared",
        };
    }

    /// <summary>
    /// Creates a diagnostic with source location information resolved from the AST node.
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

    // ── Internal state ────────────────────────────────────────────────────

    /// <summary>
    /// Tracks inference state for a single var binding candidate.
    /// </summary>
    private sealed class InferenceCandidate
    {
        public ISymbol Symbol { get; }
        public SyntaxNode DeclarationNode { get; }
        public SyntaxNode? Initializer { get; }
        public OwnershipTier? Tier { get; set; }
        public InferenceReason Reason { get; set; }

        // Conflict tracking for error messages.
        public OwnershipTier? LastConflictTier { get; set; }
        public OwnershipTier? LastConflictRequired { get; set; }
        public string? LastConflictUse { get; set; }

        public InferenceCandidate(ISymbol symbol, SyntaxNode declarationNode, SyntaxNode? initializer)
        {
            Symbol = symbol;
            DeclarationNode = declarationNode;
            Initializer = initializer;
        }
    }
}