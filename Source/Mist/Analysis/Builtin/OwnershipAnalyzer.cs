using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that enforces ownership and reference system rules against the
/// semantic model. Performs declaration validation, flow-sensitive move tracking,
/// borrow checking, lending compatibility, and mutation blocking enforcement.
/// All enforcement is compile-time with zero runtime cost.
/// </summary>
public sealed class OwnershipAnalyzer : IAnalyzer
{
    public string AnalyzerId => "OWNERSHIP";

    // ── Owned-fields collaborators ───────────────────────────────────────
    // The orchestrator delegates every owned-move site to the single
    // MoveBoundaryResolver (Req 6 AC 9). This-tier inference and ownership inference
    // are static passes invoked before flow-sensitive analysis.

    private MoveBoundaryResolver resolver = new();

    // ── Diagnostic IDs ───────────────────────────────────────────────────

    /// <summary>Access to a variable after it has been moved.</summary>
    internal static readonly DiagnosticId UseAfterMove = new("MIST_OWN_USE_AFTER_MOVE");

    /// <summary>A ref escapes its declaring scope.</summary>
    internal static readonly DiagnosticId RefEscape = new("MIST_OWN_REF_ESCAPE");

    /// <summary>Mutation attempted while frozen ref is outstanding.</summary>
    internal static readonly DiagnosticId MutationBlocked = new("MIST_OWN_MUTATION_BLOCKED");

    /// <summary>Ref assigned to a field or collection.</summary>
    internal static readonly DiagnosticId NotStorable = new("MIST_OWN_NOT_STORABLE");

    /// <summary>Multiple mutable borrows or access during ref loan.</summary>
    internal static readonly DiagnosticId ExclusiveViolation = new("MIST_OWN_EXCLUSIVE_VIOLATION");

    /// <summary>Lending from a tier that cannot provide the requested tier.</summary>
    internal static readonly DiagnosticId IncompatibleLend = new("MIST_OWN_INCOMPATIBLE_LEND");

    /// <summary>Owned type attempts to implement abstract type.</summary>
    internal static readonly DiagnosticId AbstractForbidden = new("MIST_OWN_ABSTRACT_FORBIDDEN");

    /// <summary>Ownership specifier mismatch in abstract type satisfaction.</summary>
    internal static readonly DiagnosticId SignatureMismatch = new("MIST_OWN_SIGNATURE_MISMATCH");

    /// <summary>Non-owned specifier used on return type.</summary>
    internal static readonly DiagnosticId InvalidReturnSpec = new("MIST_OWN_INVALID_RETURN_SPEC");

    /// <summary>Variable moved in one branch, accessed in another.</summary>
    internal static readonly DiagnosticId ConditionalMove = new("MIST_OWN_CONDITIONAL_MOVE");

    /// <summary>Variable moved in loop without re-assignment.</summary>
    internal static readonly DiagnosticId LoopMove = new("MIST_OWN_LOOP_MOVE");

    /// <summary>Unrestricted variable passed to ref/frozen ref parameter.</summary>
    internal static readonly DiagnosticId UnrestrictedToRef = new("MIST_OWN_UNRESTRICTED_TO_REF");

    /// <summary>Multiple ownership specifiers on same declaration.</summary>
    internal static readonly DiagnosticId DuplicateSpecifier = new("MIST_OWN_DUPLICATE_SPECIFIER");

    // ── Ownership v2 Diagnostic IDs ───────────────────────────────────────

    /// <summary>Mutable method called on a non-owned type held in an owned binding (Req 2 AC 4, Req 13 AC 1).</summary>
    internal static readonly DiagnosticId MutableHidden = new("MIST_OWN_MUTABLE_HIDDEN");

    /// <summary>Owned destination assigned a non-owned, non-clonable source (Req 1 AC 5, Req 4 AC 2, Req 13 AC 2).</summary>
    internal static readonly DiagnosticId RequiresOwned = new("MIST_OWN_REQUIRES_OWNED");

    /// <summary>An implicit clone is needed but the source type does not implement <c>IClonable&lt;T&gt;</c> (Req 8 AC 4, Req 13 AC 7).</summary>
    internal static readonly DiagnosticId NotClonable = new("MIST_OWN_NOT_CLONABLE");

    /// <summary>An owned type's method/constructor assigns a field from a non-owned source (Req 5 AC 4, Req 13 AC 4–5).</summary>
    internal static readonly DiagnosticId ContractViolation = new("MIST_OWN_CONTRACT_VIOLATION");

    /// <summary>The <c>owned</c> binding specifier is applied to a substructural (<c>linear</c>/<c>affine</c>/<c>relevant</c>) type (Req 1 AC 4, Req 13 AC 8).</summary>
    internal static readonly DiagnosticId SubstructuralBinding = new("MIST_OWN_SUBSTRUCTURAL_BINDING");

    // ── Owned-fields Diagnostic IDs ───────────────────────────────────────

    /// <summary>Explicit <c>with {tier}</c> annotation under-declares the body's requirements (Req 17 AC 11).</summary>
    internal static readonly DiagnosticId ThisTierInsufficient = new("MIST_OWN_THIS_TIER_INSUFFICIENT");

    /// <summary>Call-site receiver does not satisfy the method's inferred/declared this-tier (Req 17 AC 16).</summary>
    internal static readonly DiagnosticId ThisTierMismatch = new("MIST_OWN_THIS_TIER_MISMATCH");

    /// <summary>Ownership inference cannot find a consistent tier for a var binding (Req 18 AC 5).</summary>
    internal static readonly DiagnosticId InferenceFailure = new("MIST_OWN_INFERENCE_FAILURE");

    /// <summary>Move/borrow/freeze/readonly-ref of owned field through shared parent (Req 2 AC 5).</summary>
    internal static readonly DiagnosticId SharedExtract = new("MIST_OWN_SHARED_EXTRACT");

    /// <summary>Read of owned field through shared parent when type is not IClonable (Req 2 AC 6).</summary>
    internal static readonly DiagnosticId SharedReadNotClonable = new("MIST_OWN_SHARED_READ_NOT_CLONABLE");

    /// <summary>Static field marked <c>owned</c> (Req 1 AC 5, Req 11 AC 4).</summary>
    internal static readonly DiagnosticId StaticFieldOwned = new("MIST_OWN_STATIC_FIELD_OWNED");

    // ── Ownership v2 Diagnostic Messages ──────────────────────────────────
    // Centralized message builders for the v2 diagnostics. Emission sites
    // (MoveBoundaryResolver, RejectOwnedBindingOnSubstructuralTypes, flow-sensitive
    // analysis) call these so the message content — names and remediation hints — stays
    // consistent (Req 13).

    /// <summary>
    /// Message for <c>MIST_OWN_MUTABLE_HIDDEN</c>: a mutable method was called on a
    /// non-owned type held in an owned binding (Req 13 AC 1).
    /// </summary>
    internal static string MutableHiddenMessage(string methodName, string typeName)
    {
        return $"Cannot call mutable method '{methodName}' on '{typeName}': non-owned types appear readonly when held in an " +
        $"owned binding, so only readonly methods are visible. Declare '{typeName}' as 'owned', or assign the field " +
        $"directly from an owned source.";
    }

    /// <summary>
    /// Message for <c>MIST_OWN_REQUIRES_OWNED</c>: an owned destination was assigned a
    /// non-owned, non-clonable source (Req 13 AC 2).
    /// </summary>
    internal static string RequiresOwnedMessage(string destinationName, string sourceExpression)
    {
        return $"Owned destination '{destinationName}' cannot be assigned from '{sourceExpression}': an owned destination " +
        $"requires a uniquely-owned source — a constructor call, an owned-returning call, a move from an existing " +
        $"owned binding, or a clonable source. Move an owned value, construct a fresh value, or make the source type " +
        $"implement 'IClonable<T>'.";
    }

    /// <summary>
    /// Message for <c>MIST_OWN_NOT_CLONABLE</c>: a clone was needed but the source type
    /// does not implement <c>IClonable&lt;T&gt;</c> (Req 13 AC 7).
    /// </summary>
    internal static string NotClonableMessage(string typeName)
    {
        return $"Cannot implicitly clone source of type '{typeName}' into an owned destination because '{typeName}' does not " +
        $"implement 'IClonable<{typeName}>'. Make '{typeName}' implement 'IClonable<T>' so it can be cloned, or supply " +
        $"a uniquely-owned source instead.";
    }

    /// <summary>
    /// Message for <c>MIST_OWN_CONTRACT_VIOLATION</c>: an owned type's method/constructor
    /// assigned a field from a non-owned source (Req 13 AC 4–5).
    /// </summary>
    internal static string ContractViolationMessage(string methodName, string fieldName, string sourceExpression)
    {
        return $"Method '{methodName}' violates the owned-type cooperation contract: it assigns field '{fieldName}' from the " +
        $"non-owned source '{sourceExpression}'. Owned types may only populate fields from owned sources — make the " +
        $"parameter 'owned', clone the source, or construct a fresh value.";
    }

    /// <summary>
    /// Message for <c>MIST_OWN_SUBSTRUCTURAL_BINDING</c>: the <c>owned</c> binding specifier
    /// was applied to a substructural type (Req 13 AC 8).
    /// </summary>
    internal static string SubstructuralBindingMessage(string bindingName, string typeName)
    {
        return $"The 'owned' binding specifier cannot be applied to binding '{bindingName}' of substructural type " +
        $"'{typeName}': substructural ('linear'/'affine'/'relevant') types manage their own usage discipline via drop " +
        $"semantics and cannot be combined with 'owned' binding tracking. Remove the 'owned' specifier and rely on the " +
        $"type's substructural discipline.";
    }

    // ── Owned-fields Diagnostic Messages ──────────────────────────────────

    /// <summary>
    /// Message for <c>MIST_OWN_INFERENCE_FAILURE</c>: ownership inference could not find
    /// a consistent tier for a var binding (Req 18 AC 5).
    /// </summary>
    internal static string InferenceFailureMessage(string bindingName, string lastTier, string conflictingUse, string requiredTier)
    {
        return $"Cannot infer a consistent ownership tier for '{bindingName}': tier '{lastTier}' conflicts with " +
        $"{conflictingUse} which requires '{requiredTier}'. Add an explicit ownership specifier to resolve the ambiguity.";
    }

    /// <summary>
    /// Message for <c>MIST_OWN_THIS_TIER_MISMATCH</c>: a method call site's receiver
    /// binding does not satisfy the method's inferred/declared this-tier (Req 17 AC 16).
    /// </summary>
    internal static string ThisTierMismatchMessage(string methodName, string requiredTier, string receiverName, string actualTier)
    {
        return $"Cannot call '{methodName}' which requires '{requiredTier}' access to 'this' — the receiver " +
        $"'{receiverName}' only provides '{actualTier}' access.";
    }

    // ── Truth Table ──────────────────────────────────────────────────────

    /// <summary>
    /// Static truth table mapping (VariableOwnershipState, AccessKind) to whether the
    /// action is permitted. Encodes the ownership state machine from Requirement 11.
    /// </summary>
    private static readonly Dictionary<(VariableOwnershipState, AccessKind), bool> TruthTable = new()
    {
        // Live: all permitted
        { (VariableOwnershipState.Live, AccessKind.Read), true },
        { (VariableOwnershipState.Live, AccessKind.Write), true },
        { (VariableOwnershipState.Live, AccessKind.LiveView), true },
        { (VariableOwnershipState.Live, AccessKind.Freeze), true },
        { (VariableOwnershipState.Live, AccessKind.Borrow), true },
        { (VariableOwnershipState.Live, AccessKind.Move), true },

        // LentReadonly: read, write, live view, freeze permitted
        { (VariableOwnershipState.LentReadonly, AccessKind.Read), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.Write), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.LiveView), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.Freeze), true },
        { (VariableOwnershipState.LentReadonly, AccessKind.Borrow), false },
        { (VariableOwnershipState.LentReadonly, AccessKind.Move), false },

        // LentFrozen: read, live view, freeze permitted
        { (VariableOwnershipState.LentFrozen, AccessKind.Read), true },
        { (VariableOwnershipState.LentFrozen, AccessKind.Write), false },
        { (VariableOwnershipState.LentFrozen, AccessKind.LiveView), true },
        { (VariableOwnershipState.LentFrozen, AccessKind.Freeze), true },
        { (VariableOwnershipState.LentFrozen, AccessKind.Borrow), false },
        { (VariableOwnershipState.LentFrozen, AccessKind.Move), false },

        // LentRef: nothing permitted
        { (VariableOwnershipState.LentRef, AccessKind.Read), false },
        { (VariableOwnershipState.LentRef, AccessKind.Write), false },
        { (VariableOwnershipState.LentRef, AccessKind.LiveView), false },
        { (VariableOwnershipState.LentRef, AccessKind.Freeze), false },
        { (VariableOwnershipState.LentRef, AccessKind.Borrow), false },
        { (VariableOwnershipState.LentRef, AccessKind.Move), false },

        // Moved: nothing permitted
        { (VariableOwnershipState.Moved, AccessKind.Read), false },
        { (VariableOwnershipState.Moved, AccessKind.Write), false },
        { (VariableOwnershipState.Moved, AccessKind.LiveView), false },
        { (VariableOwnershipState.Moved, AccessKind.Freeze), false },
        { (VariableOwnershipState.Moved, AccessKind.Borrow), false },
        { (VariableOwnershipState.Moved, AccessKind.Move), false },
    };

    /// <summary>
    /// Checks whether the given action is permitted for a variable in the given state,
    /// according to the truth table. Returns <c>true</c> if permitted, <c>false</c> if rejected.
    /// </summary>
    private static bool IsActionPermitted(VariableOwnershipState state, AccessKind action)
    {
        if(TruthTable.TryGetValue((state, action), out var permitted))
        {
            return permitted;
        }

        // If not in the table, conservatively deny.
        return false;
    }

    /// <summary>
    /// Reports the appropriate diagnostic when an action is rejected by the truth table.
    /// Selects the diagnostic ID and message based on the variable's current state.
    /// </summary>
    private static void ReportTruthTableViolation(
        ISymbol variable,
        VariableOwnershipState state,
        AccessKind action,
        SyntaxNode node,

        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var (diagnosticId, message) = state switch
        {
            VariableOwnershipState.Moved =>
                (UseAfterMove, $"Variable '{variable.Name}' has been moved and cannot be accessed here"),

            VariableOwnershipState.LentRef =>
                (ExclusiveViolation,
                    $"Cannot access variable '{variable.Name}' while a borrowed ref is outstanding. " +
                    $"The owner has no access (read or write) until the ref is returned."),

            VariableOwnershipState.LentFrozen when action == AccessKind.Write =>
                (MutationBlocked,
                    $"Cannot mutate '{variable.Name}' while a frozen ref is outstanding. " +
                    $"Mutation is blocked until all frozen refs are returned."),

            VariableOwnershipState.LentFrozen when action == AccessKind.Borrow =>
                (ExclusiveViolation,
                    $"Cannot lend variable '{variable.Name}' as 'borrowed' while a frozen ref is outstanding. " +
                    $"The borrower assumes exclusive mutable access, which conflicts with the frozen guarantee."),

            VariableOwnershipState.LentFrozen when action == AccessKind.Move =>
                (MutationBlocked,
                    $"Cannot move '{variable.Name}' while a frozen ref is outstanding. " +
                    $"The new owner would start in Live state and could mutate, violating the frozen guarantee."),

            VariableOwnershipState.LentReadonly when action == AccessKind.Borrow =>
                (ExclusiveViolation,
                    $"Cannot lend variable '{variable.Name}' as 'borrowed' while a readonly ref is outstanding. " +
                    $"The borrower assumes exclusive access, which is false while a readonly ref exists."),

            VariableOwnershipState.LentReadonly when action == AccessKind.Move =>
                (ExclusiveViolation,
                    $"Cannot move '{variable.Name}' while a readonly ref is outstanding. " +
                    $"Moving would create a new owner with exclusive access, which is false while a readonly ref exists."),

            _ => (ExclusiveViolation,
                    $"Cannot perform '{action}' on variable '{variable.Name}' in state '{state}'"),
        };

        diagnostics.Add(MakeDiagnostic(diagnosticId, message, node, model));
    }

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Owned-fields orchestration (design: Modified Component 1). The analyzer keeps
        // its IAnalyzer identity ("OWNERSHIP") and delegates to specialized passes.
        // A fresh MoveBoundaryResolver per Analyze() keeps the analyzer reusable.
        resolver = new MoveBoundaryResolver();

        // Pass A: declaration validation — reject the 'owned' modifier on abstract
        // declarations ONLY (Req 5 AC 6). Owned types are free to implement abstract types
        // (e.g. IClonable).
        RejectOwnedOnAbstracts(model, diagnostics);

        // Pass B: DELETED — RejectOwnedFieldModifier removed (Req 7 AC 2).
        // The 'owned' field modifier is now valid syntax with new semantics.

        // Pass B1: reject 'owned' on static fields (Req 1 AC 5, Req 11 AC 4).
        RejectOwnedOnStaticFields(model, diagnostics);

        // Pass B2: reject the 'owned' binding specifier on substructural types
        // (Req 1 AC 4, Req 13 AC 8).
        RejectOwnedBindingOnSubstructuralTypes(model, diagnostics);

        // Pass 2: general non-storability enforcement on declarations (ref/frozen/borrowed
        // tiers cannot be stored as fields). This is NOT the deleted owned-type-specific
        // rule — it applies to all types and is retained unchanged.
        ValidateNonStorability(model, diagnostics);

        // Pass C: field-type restriction for owned types (Req 4, Req 5).
        // The behavioral cooperation contract is subsumed by provenance on owned fields
        // (Req 8 + Req 3). Only the field-type restriction survives as a standalone check.
        foreach(var ownedType in OwnedTypes(model))
        {
            FieldTypeRestrictionVerifier.Verify(ownedType, model, diagnostics);
        }

        // Pass 3: ownership signature matching validation (abstract type satisfaction).
        // Retained unchanged from v1.
        ValidateSignatureMatching(model, diagnostics);

        // Pass D-pre: this-tier inference (Req 17). Infers the minimum ownership tier
        // required on 'this' for each method by analyzing owned-field usage in the body.
        // Must run before flow-sensitive analysis so call-site checking can query tiers.
        ThisTierInferencePass.Analyze(model, diagnostics);

        // Pass D: flow-sensitive analysis (truth table retained from v1; Req 6, 7, 9).
        //   - owned move sites delegate to the MoveBoundaryResolver (Req 6 AC 9)
        //   - method calls check this-tier at call sites (Req 17 AC 15–16)
        //   - OwnershipInferencePass determines var binding tiers (Req 18)
        //   - per-field state tracking for owned bindings (Req 16)
        AnalyzeFlowSensitive(model, diagnostics);
    }

    // ── Pass A: owned-on-abstract rejection (Req 5 AC 6) ───────────

    /// <summary>
    /// Rejects the <c>owned</c> modifier on abstract declarations only
    /// (Req 5 AC 6). This is the sole surviving piece of v1's owned-type declaration
    /// validation; the field-type restriction and the "owned types cannot implement
    /// abstract types" checks are deleted under v2 (Req 10) — an owned
    /// class implementing an abstract type (e.g. <c>IClonable&lt;Self&gt;</c>) is now valid.
    /// </summary>
    private static void RejectOwnedOnAbstracts(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            switch(symbol.Kind)
            {
                case SymbolKind.Abstract:
                    RejectOwnedOnInterface(symbol, model, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Rejects the <c>owned</c> modifier on abstract declarations (Req 5 AC 6).
    /// </summary>
    private static void RejectOwnedOnInterface(ISymbol symbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        if(declaringNode is not AbstractDeclarationNode abstractDecl)
        {
            return;
        }

        if(!HasOwnedModifier(abstractDecl.Modifiers))
        {
            return;
        }

        diagnostics.Add(MakeDiagnostic(
            AbstractForbidden,
            $"The 'owned' modifier cannot be applied to abstract type '{symbol.Name}'",
            declaringNode,
            model));
    }

    // ── Pass B1: reject 'owned' on static fields (Req 1 AC 5) ────────────

    /// <summary>
    /// Rejects the <c>owned</c> modifier on static fields — ownership semantics require
    /// an instance-level owner. Emits <c>MIST_OWN_STATIC_FIELD_OWNED</c> (Req 1 AC 5,
    /// Req 11 AC 4).
    /// </summary>
    private static void RejectOwnedOnStaticFields(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            if(symbol.Kind != SymbolKind.Field)
            {
                continue;
            }

            if(symbol is not Symbol fieldSym || !fieldSym.IsOwnedField)
            {
                continue;
            }

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            if(declaringNode is not FieldDeclarationNode fieldDecl)
            {
                continue;
            }

            if(!fieldDecl.Modifiers.Any(m => m is StaticModifierModifierNode))
            {
                continue;
            }

            diagnostics.Add(MakeDiagnostic(
                StaticFieldOwned,
                $"The 'owned' modifier cannot be applied to static field '{symbol.Name}' — " +
                $"ownership semantics require an instance-level owner.",
                declaringNode,
                model));
        }
    }

    // ── Pass B2: substructural owned-binding rejection (Req 1 AC 4) ───────

    /// <summary>
    /// Rejects the <c>owned</c> binding specifier on bindings whose type is substructural
    /// (<c>linear</c>/<c>affine</c>/<c>relevant</c>) — emitting
    /// <c>MIST_OWN_SUBSTRUCTURAL_BINDING</c> (Req 1 AC 4, Req 13 AC 8).
    /// <para>
    /// Substructural types manage their own usage discipline via drop semantics; layering
    /// <c>owned</c> binding tracking on top is neither necessary nor sound. Non-substructural
    /// types (unrestricted or <c>owned</c>) are accepted, subject to provenance.
    /// </para>
    /// </summary>
    private static void RejectOwnedBindingOnSubstructuralTypes(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.LocalVariable or SymbolKind.Parameter))
            {
                continue;
            }

            if(symbol is not Symbol sym || sym.OwnershipTier != OwnershipTier.Owned)
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is null)
            {
                continue;
            }

            var discipline = GetDisciplineForType(declaredType);
            if(discipline is SubstructuralDiscipline.None)
            {
                continue;
            }

            // The type is linear, affine, or relevant — reject the owned binding.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            diagnostics.Add(MakeDiagnostic(
                SubstructuralBinding,
                SubstructuralBindingMessage(symbol.Name, declaredType.DisplayName),
                declaringNode,
                model));
        }
    }

    /// <summary>
    /// Returns the <see cref="SubstructuralDiscipline"/> of a type, resolving through
    /// <see cref="ConstructedType"/> to its generic definition if necessary.
    /// </summary>
    private static SubstructuralDiscipline GetDisciplineForType(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.Discipline,
            StructType st => st.Discipline,
            _ => SubstructuralDiscipline.None,
        };
    }

    // ── Pass C support: enumerate owned types ─────────────────────────────

    /// <summary>
    /// Enumerates the declared types (classes and structs) in user code that are declared
    /// <c>owned</c> — the inputs to the cooperation contract verifier (Req 5).
    /// Substructural types (linear, affine, relevant) inherit IsOwnedType for method
    /// visibility purposes but are excluded from the cooperation contract — they manage
    /// their own lifetime discipline independently.
    /// </summary>
    private static IEnumerable<IType> OwnedTypes(ISemanticModel model)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var declaredType = symbol.DeclaredType;
            if(declaredType is null)
            {
                continue;
            }

            if(!IsOwnedType(declaredType))
            {
                continue;
            }

            // Substructural types (linear/affine/relevant) inherit IsOwnedType but are
            // not subject to the cooperation contract — their own discipline governs them.
            if(IsSubstructural(declaredType))
            {
                continue;
            }

            yield return declaredType;
        }
    }

    /// <summary>
    /// Returns <c>true</c> if the type has a substructural discipline (linear, affine, or relevant).
    /// </summary>
    private static bool IsSubstructural(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.Discipline is not SubstructuralDiscipline.None,
            StructType st => st.Discipline is not SubstructuralDiscipline.None,
            _ => false
        };
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass D: Flow-sensitive analysis (truth table retained verbatim from v1)
    // Requirements: 6.*, 7.*, 9.*
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs flow-sensitive analysis on all method bodies in the semantic model.
    /// Walks statements in control-flow order, tracking ownership state transitions
    /// (moves, borrows) and emitting diagnostics for violations.
    /// </summary>
    private void AnalyzeFlowSensitive(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            switch(symbol.Kind)
            {
                case SymbolKind.Method:
                case SymbolKind.Constructor:
                    AnalyzeMethodBody(symbol, model, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Analyzes a single method or constructor body for ownership violations.
    /// Creates a fresh <see cref="MethodFlowState"/> and walks the body block.
    /// </summary>
    private void AnalyzeMethodBody(ISymbol methodSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        var body = declaringNode switch
        {
            MethodDeclarationNode methodDecl => methodDecl.Body.GetBlock(),
            ConstructorDeclarationNode ctorDecl => ctorDecl.Body.GetBlock(),
            _ => null,
        };

        if(body is null)
        {
            return;
        }

        var state = new MethodFlowState();

        // Register owned parameters as Live in the initial state.
        RegisterOwnedParameters(declaringNode, model, state);

        // ── Ownership inference (Req 18) ──────────────────────────────────────
        // Run ownership inference on var declarations to determine their minimum
        // ownership tier from usage analysis. The inferred tiers are stored in the
        // flow state so the this-tier call-site check can verify receivers satisfy
        // method requirements. We do NOT mutate the symbol's OwnershipTier property
        // because that would affect move/clone semantics — inference only gates
        // which methods are callable, not whether assignments are moves or clones.
        var inferredBindings = OwnershipInferencePass.InferBindings(body, model, diagnostics);
        foreach(var (symbol, binding) in inferredBindings)
        {
            if(binding.InferredTier != OwnershipTier.Unrestricted)
            {
                // Store the inferred tier for this-tier call-site checking only.
                state.InferredTiers[symbol] = binding.InferredTier;
            }
        }

        WalkBlock(body, state, model, diagnostics, isLoop: false);
    }

    /// <summary>
    /// Registers all owned parameters of a method/constructor as Live in the flow state.
    /// </summary>
    private static void RegisterOwnedParameters(SyntaxNode declaringNode, ISemanticModel model, MethodFlowState state)
    {
        var paramList = declaringNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            ConstructorDeclarationNode c => c.Parameters,
            _ => null,
        };

        if(paramList is null)
        {
            return;
        }

        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if(paramSymbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                {
                    state.VariableStates[paramSymbol] = VariableOwnershipState.Live;
                    state.BindingKinds[paramSymbol] = BindingKind.Owned;

                    // Initialize per-field state for owned parameters whose type has owned fields (Req 16 AC 1).
                    if(sym.DeclaredType is not null)
                    {
                        state.InitializeFieldStates(paramSymbol, sym.DeclaredType);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Walks a block of statements, updating the flow state for each statement.
    /// </summary>
    private void WalkBlock(
        BlockNode block,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        foreach(var stmt in block.Statements)
        {
            WalkStatement(stmt, state, model, diagnostics, isLoop);
        }
    }

    /// <summary>
    /// Walks a single statement, dispatching to the appropriate handler based on
    /// statement type. This is the main extensibility point — future tasks (6.4, 6.6,
    /// 6.8, 6.10) will add additional checks per statement type here.
    /// </summary>
    private void WalkStatement(
        StatementNode stmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        switch(stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                AnalyzeVariableDeclaration(varDeclStmt.VariableDeclaration, state, model, diagnostics, isLoop);
                break;

            case ExpressionStatementStatementNode exprStmt:
                AnalyzeExpression(exprStmt.ExpressionStatement.Expression, state, model, diagnostics, isLoop);
                break;

            case IfStatementStatementNode ifStmt:
                AnalyzeIfStatement(ifStmt.IfStatement, state, model, diagnostics, isLoop);
                break;

            case WhileStatementStatementNode whileStmt:
                AnalyzeWhileStatement(whileStmt.WhileStatement, state, model, diagnostics);
                break;

            case ForStatementStatementNode forStmt:
                AnalyzeForStatement(forStmt.ForStatement, state, model, diagnostics);
                break;

            case ForeachStatementStatementNode foreachStmt:
                AnalyzeForeachStatement(foreachStmt.ForeachStatement, state, model, diagnostics);
                break;

            case ReturnStatementStatementNode returnStmt:
                AnalyzeReturnStatement(returnStmt.ReturnStatement, state, model, diagnostics);
                break;

            default:
                // For any other statement types, walk children for expression access.
                break;
        }
    }

    // ── Variable declaration analysis ────────────────────────────────────

    /// <summary>
    /// Analyzes a variable declaration for ownership moves. If the declared variable
    /// is owned, it is registered as Live. If the initializer is an owned variable,
    /// the source is marked as Moved.
    /// </summary>
    private void AnalyzeVariableDeclaration(
        VariableDeclarationStatementNode varDecl,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        switch(varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                {
                    // Check the initializer for use-after-move before processing the move.
                    CheckExpressionForUseAfterMove(typed.Initializer, state, model, diagnostics);

                    // Check the initializer for method/constructor calls that take owned parameters.
                    CheckMethodCallsForMoves(typed.Initializer, state, model, diagnostics, isLoop);

                    // Get the declared variable symbol.
                    var declaredSymbol = model.GetDeclaredSymbol(typed);
                    if(declaredSymbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                    {
                        // ── Shared parent extraction blocking (Req 2 AC 4–5) ──────────────
                        // Moving out of an owned field through a shared parent is blocked.
                        if(IsOwnedFieldAccessOnSharedParent(typed.Initializer, state, model, out var sharedFieldSym, out var sharedRecvSym))
                        {
                            diagnostics.Add(MakeDiagnostic(
                                SharedExtract,
                                $"Cannot move out of owned field '{sharedFieldSym!.Name}' through shared binding " +
                                $"'{sharedRecvSym!.Name}' — extracting data from an owned field requires an owned " +
                                $"binding to the parent.",
                                typed,
                                model));
                            break;
                        }

                        // ── Owned parent field move-out (Req 2 AC 1, Req 16 AC 2) ────────
                        // Moving out of an owned field through an owned parent is permitted
                        // subject to per-field state. Check field state is Live first; if
                        // Moved, emit use-after-move. Then update per-field state to Moved.
                        if(IsOwnedFieldAccessOnOwnedParent(typed.Initializer, state, model, out var ownedFieldSym, out var ownedRecvSym))
                        {
                            var fieldState = state.GetFieldState(ownedRecvSym!, ownedFieldSym!);
                            if(fieldState is not null and not VariableOwnershipState.Live)
                            {
                                // Field is not Live — report appropriate violation.
                                ReportFieldStateViolation(ownedRecvSym!, ownedFieldSym!, fieldState.Value,
                                    AccessKind.Move, typed, model, diagnostics);
                                break;
                            }

                            // Register the new owned variable as Live.
                            state.VariableStates[declaredSymbol] = VariableOwnershipState.Live;
                            state.BindingKinds[declaredSymbol] = BindingKind.Owned;

                            // Initialize per-field state for the new binding if the moved-in type has owned fields.
                            if(declaredSymbol.DeclaredType is not null)
                            {
                                state.InitializeFieldStates(declaredSymbol, declaredSymbol.DeclaredType);
                            }

                            // Transition the source field to Moved (Req 16 AC 2).
                            var moveSpan = new Common.Source.SourceSpan(
                                typed.Initializer.Span.Start, typed.Initializer.Span.Length);
                            state.MoveField(ownedRecvSym!, ownedFieldSym!, moveSpan);
                            break;
                        }

                        // Register the new owned variable as Live.
                        state.VariableStates[declaredSymbol] = VariableOwnershipState.Live;
                        state.BindingKinds[declaredSymbol] = BindingKind.Owned;

                        // Initialize per-field state for the new owned binding if the type has owned fields (Req 16 AC 1).
                        if(sym.DeclaredType is not null)
                        {
                            state.InitializeFieldStates(declaredSymbol, sym.DeclaredType);
                        }

                        // Route the initializer through the single move boundary (Req 6 AC 9):
                        // it resolves provenance (owned move / value copy / clone / default),
                        // marks a consumed owned source Moved, records implicit-clone
                        // annotations, and emits MIST_OWN_REQUIRES_OWNED / MIST_OWN_NOT_CLONABLE
                        // for a non-owned, non-clonable source.
                        ResolveOwnedBindingInit(typed.Initializer, sym, state, model, diagnostics);
                    }
                    else if(declaredSymbol is Symbol refSym &&
                             refSym.OwnershipTier is OwnershipTier.Frozen or OwnershipTier.Borrowed or OwnershipTier.ReadonlyRef)
                    {
                        // ── Shared parent extraction blocking (Req 2 AC 4–5) ──────────────
                        // Borrowing, freezing, or taking a readonly ref of an owned field through
                        // a shared parent is blocked — these operations require exclusive access
                        // that only an owned parent can guarantee.
                        if(IsOwnedFieldAccessOnSharedParent(typed.Initializer, state, model, out var sharedFieldSym, out var sharedRecvSym))
                        {
                            var tierName = FormatOwnershipTier(refSym.OwnershipTier);
                            diagnostics.Add(MakeDiagnostic(
                                SharedExtract,
                                $"Cannot take '{tierName}' reference to owned field '{sharedFieldSym!.Name}' through " +
                                $"shared binding '{sharedRecvSym!.Name}' — extracting data from an owned field requires " +
                                $"an owned binding to the parent.",
                                typed,
                                model));
                            break;
                        }

                        // Local frozen, borrowed, or readonly ref variable declaration creates a borrow from the initializer.
                        var sourceSymbol = TryResolveIdentifierSymbol(typed.Initializer, model);

                        // ── Owned parent field borrow/freeze/readonly-ref (Req 2 AC 1, Req 16) ──
                        // If the initializer is a field access on an owned parent, handle per-field
                        // state tracking for borrow, freeze, and readonly ref operations.
                        if(sourceSymbol is null &&
                            IsOwnedFieldAccessOnOwnedParent(typed.Initializer, state, model, out var ownedFieldRefSym, out var ownedRecvRefSym))
                        {
                            var fieldState = state.GetFieldState(ownedRecvRefSym!, ownedFieldRefSym!);
                            if(refSym.OwnershipTier == OwnershipTier.ReadonlyRef)
                            {
                                // Readonly ref of field: check field state permits LiveView.
                                if(fieldState is not null && !IsActionPermitted(fieldState.Value, AccessKind.LiveView))
                                {
                                    ReportFieldStateViolation(ownedRecvRefSym!, ownedFieldRefSym!, fieldState.Value,
                                        AccessKind.LiveView, typed, model, diagnostics);
                                }
                                else
                                {
                                    state.ReadonlyRefField(ownedRecvRefSym!, ownedFieldRefSym!, declaredSymbol);
                                }
                            }
                            else if(refSym.OwnershipTier == OwnershipTier.Frozen)
                            {
                                // Freeze field: check field state permits Freeze.
                                if(fieldState is not null && !IsActionPermitted(fieldState.Value, AccessKind.Freeze))
                                {
                                    ReportFieldStateViolation(ownedRecvRefSym!, ownedFieldRefSym!, fieldState.Value,
                                        AccessKind.Freeze, typed, model, diagnostics);
                                }
                                else
                                {
                                    state.FreezeField(ownedRecvRefSym!, ownedFieldRefSym!, declaredSymbol);
                                }
                            }
                            else // Borrowed
                            {
                                // Borrow field: check field state permits Borrow.
                                if(fieldState is not null && !IsActionPermitted(fieldState.Value, AccessKind.Borrow))
                                {
                                    ReportFieldStateViolation(ownedRecvRefSym!, ownedFieldRefSym!, fieldState.Value,
                                        AccessKind.Borrow, typed, model, diagnostics);
                                }
                                else
                                {
                                    state.BorrowField(ownedRecvRefSym!, ownedFieldRefSym!, declaredSymbol);
                                }
                            }

                            break;
                        }

                        if(sourceSymbol is not null &&
                            sourceSymbol is Symbol sourceSym &&
                            sourceSym.OwnershipTier is OwnershipTier.Owned or OwnershipTier.Borrowed)
                        {
                            if(refSym.OwnershipTier == OwnershipTier.ReadonlyRef)
                            {
                                // Readonly ref: lender transitions to LentReadonly.
                                // Check truth table: is LiveView permitted in the current state?
                                if(state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
                                {
                                    if(!IsActionPermitted(currentState, AccessKind.LiveView))
                                    {
                                        ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.LiveView, typed, model, diagnostics);
                                    }
                                    else
                                    {
                                        // Only transition to LentReadonly if currently Live.
                                        // If already LentReadonly or LentFrozen, stay in that state (more restrictive).
                                        if(currentState == VariableOwnershipState.Live)
                                        {
                                            state.VariableStates[sourceSymbol] = VariableOwnershipState.LentReadonly;
                                        }

                                        state.ActiveBorrows.Add(new BorrowEntry(
                                            sourceSymbol, declaredSymbol, OwnershipTier.ReadonlyRef, state.ScopeDepth));
                                    }
                                }
                                else
                                {
                                    // Variable not tracked yet — register the borrow without state transition.
                                    state.ActiveBorrows.Add(new BorrowEntry(
                                        sourceSymbol, declaredSymbol, OwnershipTier.ReadonlyRef, state.ScopeDepth));
                                }
                            }
                            else if(refSym.OwnershipTier == OwnershipTier.Frozen)
                            {
                                // Frozen borrow: check truth table before transitioning.
                                if(state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
                                {
                                    if(!IsActionPermitted(currentState, AccessKind.Freeze))
                                    {
                                        ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.Freeze, typed, model, diagnostics);
                                    }
                                    else
                                    {
                                        state.VariableStates[sourceSymbol] = VariableOwnershipState.LentFrozen;
                                        state.ActiveBorrows.Add(new BorrowEntry(
                                            sourceSymbol, declaredSymbol, OwnershipTier.Frozen, state.ScopeDepth));
                                    }
                                }
                                else
                                {
                                    // Not tracked — apply the transition.
                                    state.VariableStates[sourceSymbol] = VariableOwnershipState.LentFrozen;
                                    state.ActiveBorrows.Add(new BorrowEntry(
                                        sourceSymbol, declaredSymbol, OwnershipTier.Frozen, state.ScopeDepth));
                                }
                            }
                            else // Borrowed
                            {
                                // Borrowed: check truth table before transitioning.
                                if(state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
                                {
                                    if(!IsActionPermitted(currentState, AccessKind.Borrow))
                                    {
                                        ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.Borrow, typed, model, diagnostics);
                                    }
                                    else
                                    {
                                        // Check for existing ref borrow (only one at a time).
                                        if(HasActiveRefBorrow(sourceSymbol, state))
                                        {
                                            diagnostics.Add(MakeDiagnostic(
                                                ExclusiveViolation,
                                                $"Cannot lend variable '{sourceSymbol.Name}' as 'borrowed' — another borrow is already outstanding. " +
                                                $"Only one mutable borrow is permitted at a time.",
                                                typed,
                                                model));
                                        }
                                        else
                                        {
                                            state.VariableStates[sourceSymbol] = VariableOwnershipState.LentRef;
                                            state.ActiveBorrows.Add(new BorrowEntry(
                                                sourceSymbol, declaredSymbol, OwnershipTier.Borrowed, state.ScopeDepth));
                                        }
                                    }
                                }
                                else
                                {
                                    // Not tracked — apply the transition.
                                    if(HasActiveRefBorrow(sourceSymbol, state))
                                    {
                                        diagnostics.Add(MakeDiagnostic(
                                            ExclusiveViolation,
                                            $"Cannot lend variable '{sourceSymbol.Name}' as 'borrowed' — another borrow is already outstanding. " +
                                            $"Only one mutable borrow is permitted at a time.",
                                            typed,
                                            model));
                                    }
                                    else
                                    {
                                        state.VariableStates[sourceSymbol] = VariableOwnershipState.LentRef;
                                        state.ActiveBorrows.Add(new BorrowEntry(
                                            sourceSymbol, declaredSymbol, OwnershipTier.Borrowed, state.ScopeDepth));
                                    }
                                }
                            }
                        }
                        else if(sourceSymbol is not null &&
                                 state.BindingKinds.TryGetValue(sourceSymbol, out var sourceBindingKind) &&
                                 sourceBindingKind == BindingKind.Unrestricted)
                        {
                            // Unrestricted binding restriction: cannot take borrowed or frozen reference
                            // to an unrestricted binding. Readonly ref to the variable itself is permitted.
                            if(refSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.Frozen)
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    UnrestrictedToRef,
                                    $"Cannot lend unrestricted binding '{sourceSymbol.Name}' as '{FormatOwnershipTier(refSym.OwnershipTier)}' — " +
                                    $"only owned bindings can guarantee exclusive access.",
                                    typed,
                                    model));
                            }
                            // Readonly ref to an unrestricted binding (direct/top-level) is permitted — no error.
                        }
                        else
                        {
                            // Check if the initializer is a field access on an unrestricted binding.
                            // e.g., borrowed var y = x.field where x is unrestricted.
                            var receiverSymbol = TryResolveFieldAccessReceiver(typed.Initializer, model);
                            if(receiverSymbol is not null &&
                                state.BindingKinds.TryGetValue(receiverSymbol, out var receiverBindingKind) &&
                                receiverBindingKind == BindingKind.Unrestricted)
                            {
                                if(refSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.Frozen)
                                {
                                    // Cannot take borrowed or frozen reference to any field in the
                                    // object graph of an unrestricted binding.
                                    diagnostics.Add(MakeDiagnostic(
                                        UnrestrictedToRef,
                                        $"Cannot take '{FormatOwnershipTier(refSym.OwnershipTier)}' reference to a field of " +
                                        $"unrestricted binding '{receiverSymbol.Name}' — only owned bindings can borrow into the object graph.",
                                        typed,
                                        model));
                                }
                                // Readonly ref to direct (top-level) fields of an unrestricted binding is permitted.
                            }
                        }
                    }
                    else if(declaredSymbol is not null)
                    {
                        // Unrestricted binding — register binding kind for access rule enforcement.
                        state.BindingKinds[declaredSymbol] = BindingKind.Unrestricted;

                        // If the initializer is an owned variable, this is a move (owned → unrestricted).
                        // The source becomes dead — the destination holds the value without ownership tracking.
                        var sourceSymbol = TryResolveIdentifierSymbol(typed.Initializer, model);
                        if(sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
                        {
                            PerformMove(sourceSymbol, typed.Initializer, state, model, diagnostics);
                        }
                    }

                    break;
                }
            case InferredVariableDeclarationVariableDeclarationStatementNode inferred:
                {
                    // Check the initializer for use-after-move.
                    CheckExpressionForUseAfterMove(inferred.Initializer, state, model, diagnostics);

                    // Check the initializer for method/constructor calls that take owned parameters.
                    CheckMethodCallsForMoves(inferred.Initializer, state, model, diagnostics, isLoop);

                    // Get the declared variable symbol.
                    var declaredSymbol = model.GetDeclaredSymbol(inferred);
                    if(declaredSymbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                    {
                        // ── Shared parent extraction blocking (Req 2 AC 4–5) ──────────────
                        // Moving out of an owned field through a shared parent is blocked.
                        if(IsOwnedFieldAccessOnSharedParent(inferred.Initializer, state, model, out var sharedFieldSym, out var sharedRecvSym))
                        {
                            diagnostics.Add(MakeDiagnostic(
                                SharedExtract,
                                $"Cannot move out of owned field '{sharedFieldSym!.Name}' through shared binding " +
                                $"'{sharedRecvSym!.Name}' — extracting data from an owned field requires an owned " +
                                $"binding to the parent.",
                                inferred,
                                model));
                            break;
                        }

                        // ── Owned parent field move-out (Req 2 AC 1, Req 16 AC 2) ────────
                        // Moving out of an owned field through an owned parent is permitted
                        // subject to per-field state. Check field state is Live first.
                        if(IsOwnedFieldAccessOnOwnedParent(inferred.Initializer, state, model, out var ownedFieldSym, out var ownedRecvSym))
                        {
                            var fieldState = state.GetFieldState(ownedRecvSym!, ownedFieldSym!);
                            if(fieldState is not null and not VariableOwnershipState.Live)
                            {
                                ReportFieldStateViolation(ownedRecvSym!, ownedFieldSym!, fieldState.Value,
                                    AccessKind.Move, inferred, model, diagnostics);
                                break;
                            }

                            // Register the new owned variable as Live.
                            state.VariableStates[declaredSymbol] = VariableOwnershipState.Live;
                            state.BindingKinds[declaredSymbol] = BindingKind.Owned;

                            // Initialize per-field state for the new binding if the moved-in type has owned fields.
                            if(declaredSymbol.DeclaredType is not null)
                            {
                                state.InitializeFieldStates(declaredSymbol, declaredSymbol.DeclaredType);
                            }

                            // Transition the source field to Moved (Req 16 AC 2).
                            var moveSpan = new Common.Source.SourceSpan(
                                inferred.Initializer.Span.Start, inferred.Initializer.Span.Length);
                            state.MoveField(ownedRecvSym!, ownedFieldSym!, moveSpan);
                            break;
                        }

                        // Register the new owned variable as Live.
                        state.VariableStates[declaredSymbol] = VariableOwnershipState.Live;
                        state.BindingKinds[declaredSymbol] = BindingKind.Owned;

                        // Initialize per-field state for the new owned binding if the type has owned fields (Req 16 AC 1).
                        if(sym.DeclaredType is not null)
                        {
                            state.InitializeFieldStates(declaredSymbol, sym.DeclaredType);
                        }

                        // Route the initializer through the single move boundary (Req 6 AC 9).
                        ResolveOwnedBindingInit(inferred.Initializer, sym, state, model, diagnostics);
                    }
                    else if(declaredSymbol is Symbol refSym &&
                             refSym.OwnershipTier is OwnershipTier.Frozen or OwnershipTier.Borrowed or OwnershipTier.ReadonlyRef)
                    {
                        // ── Shared parent extraction blocking (Req 2 AC 4–5) ──────────────
                        // Borrowing, freezing, or taking a readonly ref of an owned field through
                        // a shared parent is blocked — these operations require exclusive access
                        // that only an owned parent can guarantee.
                        if(IsOwnedFieldAccessOnSharedParent(inferred.Initializer, state, model, out var sharedFieldSym3, out var sharedRecvSym3))
                        {
                            var tierName = FormatOwnershipTier(refSym.OwnershipTier);
                            diagnostics.Add(MakeDiagnostic(
                                SharedExtract,
                                $"Cannot take '{tierName}' reference to owned field '{sharedFieldSym3!.Name}' through " +
                                $"shared binding '{sharedRecvSym3!.Name}' — extracting data from an owned field requires " +
                                $"an owned binding to the parent.",
                                inferred,
                                model));
                            break;
                        }

                        // For ref-tier inferred bindings, handle lending from owned/borrowed sources.
                        var sourceSymbolRef = TryResolveIdentifierSymbol(inferred.Initializer, model);
                        if(sourceSymbolRef is not null &&
                            sourceSymbolRef is Symbol sourceRefSym &&
                            sourceRefSym.OwnershipTier is OwnershipTier.Owned or OwnershipTier.Borrowed)
                        {
                            if(refSym.OwnershipTier == OwnershipTier.ReadonlyRef)
                            {
                                if(state.VariableStates.TryGetValue(sourceSymbolRef, out var currentState))
                                {
                                    if(!IsActionPermitted(currentState, AccessKind.LiveView))
                                    {
                                        ReportTruthTableViolation(sourceSymbolRef, currentState, AccessKind.LiveView, inferred, model, diagnostics);
                                    }
                                    else
                                    {
                                        if(currentState == VariableOwnershipState.Live)
                                        {
                                            state.VariableStates[sourceSymbolRef] = VariableOwnershipState.LentReadonly;
                                        }

                                        state.ActiveBorrows.Add(new BorrowEntry(
                                            sourceSymbolRef, declaredSymbol, OwnershipTier.ReadonlyRef, state.ScopeDepth));
                                    }
                                }
                                else
                                {
                                    state.ActiveBorrows.Add(new BorrowEntry(
                                        sourceSymbolRef, declaredSymbol, OwnershipTier.ReadonlyRef, state.ScopeDepth));
                                }
                            }
                            else if(refSym.OwnershipTier == OwnershipTier.Frozen)
                            {
                                if(state.VariableStates.TryGetValue(sourceSymbolRef, out var currentState))
                                {
                                    if(!IsActionPermitted(currentState, AccessKind.Freeze))
                                    {
                                        ReportTruthTableViolation(sourceSymbolRef, currentState, AccessKind.Freeze, inferred, model, diagnostics);
                                    }
                                    else
                                    {
                                        state.VariableStates[sourceSymbolRef] = VariableOwnershipState.LentFrozen;
                                        state.ActiveBorrows.Add(new BorrowEntry(
                                            sourceSymbolRef, declaredSymbol, OwnershipTier.Frozen, state.ScopeDepth));
                                    }
                                }
                                else
                                {
                                    state.VariableStates[sourceSymbolRef] = VariableOwnershipState.LentFrozen;
                                    state.ActiveBorrows.Add(new BorrowEntry(
                                        sourceSymbolRef, declaredSymbol, OwnershipTier.Frozen, state.ScopeDepth));
                                }
                            }
                            else // Borrowed
                            {
                                if(state.VariableStates.TryGetValue(sourceSymbolRef, out var currentState))
                                {
                                    if(!IsActionPermitted(currentState, AccessKind.Borrow))
                                    {
                                        ReportTruthTableViolation(sourceSymbolRef, currentState, AccessKind.Borrow, inferred, model, diagnostics);
                                    }
                                    else
                                    {
                                        if(HasActiveRefBorrow(sourceSymbolRef, state))
                                        {
                                            diagnostics.Add(MakeDiagnostic(
                                                ExclusiveViolation,
                                                $"Cannot lend variable '{sourceSymbolRef.Name}' as 'borrowed' — another borrow is already outstanding. " +
                                                $"Only one mutable borrow is permitted at a time.",
                                                inferred,
                                                model));
                                        }
                                        else
                                        {
                                            state.VariableStates[sourceSymbolRef] = VariableOwnershipState.LentRef;
                                            state.ActiveBorrows.Add(new BorrowEntry(
                                                sourceSymbolRef, declaredSymbol, OwnershipTier.Borrowed, state.ScopeDepth));
                                        }
                                    }
                                }
                                else
                                {
                                    if(HasActiveRefBorrow(sourceSymbolRef, state))
                                    {
                                        diagnostics.Add(MakeDiagnostic(
                                            ExclusiveViolation,
                                            $"Cannot lend variable '{sourceSymbolRef.Name}' as 'borrowed' — another borrow is already outstanding. " +
                                            $"Only one mutable borrow is permitted at a time.",
                                            inferred,
                                            model));
                                    }
                                    else
                                    {
                                        state.VariableStates[sourceSymbolRef] = VariableOwnershipState.LentRef;
                                        state.ActiveBorrows.Add(new BorrowEntry(
                                            sourceSymbolRef, declaredSymbol, OwnershipTier.Borrowed, state.ScopeDepth));
                                    }
                                }
                            }
                        }
                        else if(sourceSymbolRef is not null &&
                                 state.BindingKinds.TryGetValue(sourceSymbolRef, out var sourceBindingKindRef) &&
                                 sourceBindingKindRef == BindingKind.Unrestricted)
                        {
                            if(refSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.Frozen)
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    UnrestrictedToRef,
                                    $"Cannot lend unrestricted binding '{sourceSymbolRef.Name}' as '{FormatOwnershipTier(refSym.OwnershipTier)}' — " +
                                    $"only owned bindings can guarantee exclusive access.",
                                    inferred,
                                    model));
                            }
                        }
                        else
                        {
                            // Check if the initializer is a field access on an unrestricted binding.
                            var receiverSymbol = TryResolveFieldAccessReceiver(inferred.Initializer, model);
                            if(receiverSymbol is not null &&
                                state.BindingKinds.TryGetValue(receiverSymbol, out var receiverBindingKind) &&
                                receiverBindingKind == BindingKind.Unrestricted)
                            {
                                if(refSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.Frozen)
                                {
                                    diagnostics.Add(MakeDiagnostic(
                                        UnrestrictedToRef,
                                        $"Cannot take '{FormatOwnershipTier(refSym.OwnershipTier)}' reference to a field of " +
                                        $"unrestricted binding '{receiverSymbol.Name}' — only owned bindings can borrow into the object graph.",
                                        inferred,
                                        model));
                                }
                            }
                        }
                    }
                    else if(declaredSymbol is not null)
                    {
                        // Unrestricted binding — register binding kind for access rule enforcement.
                        state.BindingKinds[declaredSymbol] = BindingKind.Unrestricted;

                        // ── Shared parent read-not-clonable check (Req 2 AC 6) ───────────
                        // Reading an owned field through a shared parent requires the field's type
                        // to implement IClonable. If NOT IClonable, the field cannot be cloned and
                        // cannot be moved out through a shared parent.
                        if(IsOwnedFieldAccessOnSharedParent(inferred.Initializer, state, model, out var sharedFieldSym2, out var sharedRecvSym2))
                        {
                            var fieldType = sharedFieldSym2!.DeclaredType;
                            if(fieldType is not null && !ImplementsIClonable(fieldType) && !IsValueType(fieldType))
                            {
                                diagnostics.Add(MakeDiagnostic(
                                    SharedReadNotClonable,
                                    $"Cannot read owned field '{sharedFieldSym2.Name}' through shared binding " +
                                    $"'{sharedRecvSym2!.Name}' — the field's type '{fieldType.DisplayName}' does not " +
                                    $"implement 'IClonable<{fieldType.DisplayName}>' and cannot be moved out without " +
                                    $"an owned binding to the parent.",
                                    inferred,
                                    model));
                                break;
                            }
                        }

                        // If the initializer is an owned variable, this is a move (owned → unrestricted).
                        // The source becomes dead — the destination holds the value without ownership tracking.
                        var sourceSymbol = TryResolveIdentifierSymbol(inferred.Initializer, model);
                        if(sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
                        {
                            PerformMove(sourceSymbol, inferred.Initializer, state, model, diagnostics);
                        }
                    }

                    break;
                }
        }
    }

    // ── Owned move-site delegation to MoveBoundaryResolver (Req 6 AC 9) ───

    /// <summary>
    /// Resolves an owned-binding initializer through the single <see cref="MoveBoundaryResolver"/>
    /// (site <see cref="OwnedMoveSite.OwnedBindingInit"/>, Req 1, Req 6). The resolver decides
    /// provenance and clone insertion, marks a consumed owned source <c>Moved</c> in
    /// <paramref name="state"/>, records implicit-clone annotations for the emitter, and emits
    /// <c>MIST_OWN_REQUIRES_OWNED</c> / <c>MIST_OWN_NOT_CLONABLE</c> for a non-owned,
    /// non-clonable source. This replaces v1's bespoke <c>YieldsUniqueOwnership</c> /
    /// <c>PerformMove</c> pairing at this site.
    /// </summary>
    private void ResolveOwnedBindingInit(
        SyntaxNode initializer,
        Symbol destination,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        _ = resolver.Resolve(
            initializer,
            destination.DeclaredType,

            state,
            model,
            diagnostics);
    }

    // ── Expression analysis ──────────────────────────────────────────────

    /// <summary>
    /// Analyzes a top-level expression statement for ownership operations.
    /// Handles assignments (which may be moves) and method calls (which may
    /// pass owned arguments).
    /// </summary>
    private void AnalyzeExpression(
        ExpressionNode expr,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Lambda-form expressions introduce their own body scope; closure capture
        // ownership is handled by the type checker's capture rules, not flow analysis.
        if(expr.AsAssignmentOrNull() is { } assignment)
        {
            AnalyzeAssignment(assignment, state, model, diagnostics, isLoop);
        }
    }

    /// <summary>
    /// Analyzes an assignment node. If this is a reassignment to an owned variable,
    /// it may be a move (from the RHS) and/or a re-assignment (restoring the LHS).
    /// </summary>
    private void AnalyzeAssignment(
        AssignmentNode assignment,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        if(assignment.Value is not null)
        {
            // This is an assignment: target = value. The RHS is now a full
            // ExpressionNode (widened to reach lambdas). A lambda RHS has no
            // move/ownership effect on the assignment, so resolve the assignment
            // form and skip when it is a lambda.
            var rhsAssignment = assignment.Value.AsAssignmentOrNull();

            // First, check the RHS for use-after-move.
            if(rhsAssignment is not null)
            {
                CheckAssignmentValueForUseAfterMove(rhsAssignment, state, model, diagnostics);
            }

            // Resolve the LHS target symbol.
            var lhsSymbol = TryResolveAssignmentTarget(assignment.Target, model);

            // Resolve the RHS source symbol.
            var rhsSymbol = rhsAssignment is not null
                ? TryResolveAssignmentValueSymbol(rhsAssignment, model)
                : null;

            // Non-storability check: if the RHS is a ref-tier variable and the LHS
            // is a field, emit MIST_OWN_NOT_STORABLE.
            if(rhsSymbol is Symbol rhsSym &&
                rhsSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.ReadonlyRef or OwnershipTier.Frozen)
            {
                var targetFieldSymbol = TryResolveFieldTarget(assignment.Target, model);
                if(targetFieldSymbol is not null && targetFieldSymbol.Kind == SymbolKind.Field)
                {
                    var tierName = FormatOwnershipTier(rhsSym.OwnershipTier);
                    diagnostics.Add(MakeDiagnostic(
                        NotStorable,
                        $"Cannot assign '{tierName}' variable '{rhsSym.Name}' to field '{targetFieldSymbol.Name}' because ref-tier references are non-storable",
                        assignment,
                        model));
                }
            }

            // If the LHS is an owned variable, this assignment restores it to Live.
            if(lhsSymbol is not null && IsOwnedVariable(lhsSymbol))
            {
                state.VariableStates[lhsSymbol] = VariableOwnershipState.Live;
                _ = state.MoveLocations.Remove(lhsSymbol);
            }

            // Mutation blocking check: if the assignment target is a field access on a
            // variable in a restricted state, use the truth table to check Write permission.
            var receiverOfFieldAssign = TryResolveFieldAccessReceiver(assignment.Target, model);
            if(receiverOfFieldAssign is not null &&
                state.VariableStates.TryGetValue(receiverOfFieldAssign, out var recvState))
            {
                if(!IsActionPermitted(recvState, AccessKind.Write))
                {
                    ReportTruthTableViolation(receiverOfFieldAssign, recvState, AccessKind.Write, assignment, model, diagnostics);
                }
            }

            // Field assignment provenance (Req 4, Req 2 AC 2): assigning to an owned field
            // requires the source to yield an owned value. This applies regardless of whether
            // the parent binding is owned or shared (Req 2 AC 2 — shared parent can write with
            // provenance). Route the RHS through the single move boundary (site
            // OwnedFieldAssignment). The resolver emits MIST_OWN_REQUIRES_OWNED /
            // MIST_OWN_NOT_CLONABLE for a non-owned, non-clonable source, records
            // implicit-clone annotations, and consumes an owned source.
            if(assignment.Value is not null &&
                receiverOfFieldAssign is not null)
            {
                var targetField = TryResolveFieldTarget(assignment.Target, model);
                var isOwnedParent = IsOwnedVariable(receiverOfFieldAssign);
                var isOwnedField = targetField is Symbol tfCheck && tfCheck.IsOwnedField;

                if(isOwnedParent || isOwnedField)
                {
                    // Per-field write state check (Req 16 AC 3–5, Req 2 AC 1):
                    // Fields in LentRef or LentFrozen cannot be written to (outstanding borrow blocks mutation).
                    // Fields in Moved CAN be written to — that's the restoration mechanism (Req 16 AC 6).
                    if(targetField is Symbol tfs && tfs.IsOwnedField && isOwnedParent)
                    {
                        var currentFieldState = state.GetFieldState(receiverOfFieldAssign, targetField);
                        if(currentFieldState is VariableOwnershipState.LentRef or VariableOwnershipState.LentFrozen)
                        {
                            ReportFieldStateViolation(receiverOfFieldAssign, targetField, currentFieldState.Value,
                                AccessKind.Write, assignment, model, diagnostics);
                        }
                    }

                    var resolution = resolver.Resolve(
                        assignment.Value,
                        targetField?.DeclaredType,

                        state,
                        model,
                        diagnostics);

                    // Per-field state restoration (Req 16 AC 6, Req 2 AC 1):
                    // After the resolver approves a write to an owned field through an owned parent,
                    // restore the field from Moved to Live (if it was Moved).
                    if(isOwnedParent && targetField is Symbol restoredField && restoredField.IsOwnedField &&
                        resolution.Outcome != MoveOutcome.Error)
                    {
                        state.RestoreField(receiverOfFieldAssign, targetField);
                    }
                }
            }

            // this.field provenance (Req 4, Req 8): when the receiver is 'this' (not resolved
            // by TryResolveFieldAccessReceiver since ThisExpressionPrimaryNode is not an identifier),
            // resolve the field directly and route through the move boundary if it's an owned field.
            // This replaces the old CooperationContractVerifier's per-method provenance check.
            if(assignment.Value is not null && receiverOfFieldAssign is null)
            {
                var thisField = TryResolveThisFieldTarget(assignment.Target, model);
                if(thisField is Symbol thisFieldSym && thisFieldSym.IsOwnedField)
                {
                    // Guard: only process if the source expression has a concrete resolvable type.
                    // Skip TypeParameterType (unsubstituted generic parameters from open generic
                    // methods/types) — these are processed during monomorphization and don't need
                    // provenance enforcement at the generic definition level.
                    var sourceType = model.GetTypeOf(assignment.Value);
                    if(sourceType is not null and not ErrorType
                        and not TypeParameterType)
                    {
                        _ = resolver.Resolve(
                            assignment.Value,
                            thisFieldSym.DeclaredType,

                            state,
                            model,
                            diagnostics);
                    }
                }
            }

            // If the RHS is an owned variable being assigned to an owned or unrestricted LHS, it's a move.
            // Owned → owned: ownership transfers. Owned → unrestricted: source becomes dead.
            if(rhsSymbol is not null && IsOwnedVariable(rhsSymbol) && lhsSymbol is not null)
            {
                if(IsOwnedVariable(lhsSymbol))
                {
                    PerformMove(rhsSymbol, assignment, state, model, diagnostics);
                }
                else if(state.BindingKinds.TryGetValue(lhsSymbol, out var lhsKind) &&
                         lhsKind == BindingKind.Unrestricted)
                {
                    PerformMove(rhsSymbol, assignment, state, model, diagnostics);
                }
            }
        }
        else
        {
            // No assignment value — this is just an expression (e.g., method call).
            // Check the target expression for use-after-move and method call moves.
            CheckLogicalOrForUseAfterMove(assignment.Target, state, model, diagnostics);
            CheckMethodCallsForMoves(assignment.Target, state, model, diagnostics, isLoop);
        }
    }

    // ── Control flow analysis ────────────────────────────────────────────

    /// <summary>
    /// Analyzes an if statement by forking state at the branch point and merging
    /// at the join. If a variable is moved in one branch but not the other, the
    /// merged state marks it as Moved (conservative).
    /// </summary>
    private void AnalyzeIfStatement(
        IfStatementNode ifStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Check the condition for use-after-move.
        CheckExpressionForUseAfterMove(ifStmt.Condition, state, model, diagnostics);

        // Fork state for the then-branch.
        var thenState = state.Fork();

        // Walk the then-branch.
        state.EnterScope();
        WalkBlock(ifStmt.ThenBranch, state, model, diagnostics, isLoop);
        state.ExitScope();

        // Handle else branch.
        if(ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            // Fork from the pre-if state for the else branch.
            _ = thenState.Fork();

            // Swap: put the then-branch result in thenState, walk else on elseState.
            // Actually, we need to walk else on the pre-if state (thenState is the fork).
            // Let's restructure: state currently has the then-branch result.
            // We need to walk else on the pre-if state (thenState).
            thenState.EnterScope();
            WalkBlock(elseBlock.ElseBlock, thenState, model, diagnostics, isLoop);
            thenState.ExitScope();

            // Merge: state (then result) merges with thenState (else result).
            state.Merge(thenState);
        }
        else if(ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            // Treat else-if as a nested if in the else branch.
            var elseIfState = thenState.Fork();
            AnalyzeIfStatement(elseIf.ElseIf, elseIfState, model, diagnostics, isLoop);

            // Merge the then-branch result with the else-if result.
            state.Merge(elseIfState);
        }
        else
        {
            // No else branch: merge with the pre-if state (variable might not be moved
            // if the then-branch is not taken).
            state.Merge(thenState);
        }
    }

    /// <summary>
    /// Analyzes a while loop. Moves inside the loop body without re-assignment
    /// emit <c>MIST_OWN_LOOP_MOVE</c>.
    /// </summary>
    private void AnalyzeWhileStatement(
        WhileStatementNode whileStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the condition for use-after-move.
        CheckExpressionForUseAfterMove(whileStmt.Condition, state, model, diagnostics);

        // Snapshot which owned variables are Live before the loop body.
        var liveBeforeLoop = new Dictionary<ISymbol, VariableOwnershipState>();
        foreach(var (symbol, varState) in state.VariableStates)
        {
            if(varState == VariableOwnershipState.Live && IsOwnedVariable(symbol))
            {
                liveBeforeLoop[symbol] = varState;
            }
        }

        // Walk the loop body with isLoop=true so nested constructs know they're in a loop.
        state.EnterScope();
        WalkBlock(whileStmt.Body, state, model, diagnostics, isLoop: true);
        state.ExitScope();

        // After walking the body, any variable that was Live before and is now Moved
        // was moved without re-assignment in the same iteration → report MIST_OWN_LOOP_MOVE.
        ReportUnresolvedLoopMoves(liveBeforeLoop, state, whileStmt, model, diagnostics);
    }

    /// <summary>
    /// Analyzes a for loop. Moves inside the loop body without re-assignment
    /// emit <c>MIST_OWN_LOOP_MOVE</c>.
    /// </summary>
    private void AnalyzeForStatement(
        ForStatementNode forStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check initializer for use-after-move.
        if(forStmt.Initializer is not null)
        {
            foreach(var child in forStmt.Initializer.Children())
            {
                if(child is ExpressionNode expr)
                {
                    CheckExpressionForUseAfterMove(expr, state, model, diagnostics);
                }
            }
        }

        // Check condition for use-after-move.
        if(forStmt.Condition is not null)
        {
            CheckExpressionForUseAfterMove(forStmt.Condition, state, model, diagnostics);
        }

        // Snapshot which owned variables are Live before the loop body.
        var liveBeforeLoop = new Dictionary<ISymbol, VariableOwnershipState>();
        foreach(var (symbol, varState) in state.VariableStates)
        {
            if(varState == VariableOwnershipState.Live && IsOwnedVariable(symbol))
            {
                liveBeforeLoop[symbol] = varState;
            }
        }

        // Walk the loop body with isLoop=true.
        state.EnterScope();
        WalkBlock(forStmt.Body, state, model, diagnostics, isLoop: true);
        state.ExitScope();

        // After walking the body, any variable that was Live before and is now Moved
        // was moved without re-assignment in the same iteration → report MIST_OWN_LOOP_MOVE.
        ReportUnresolvedLoopMoves(liveBeforeLoop, state, forStmt, model, diagnostics);

        // Check iterator for use-after-move.
        if(forStmt.Iterator is not null)
        {
            CheckExpressionForUseAfterMove(forStmt.Iterator, state, model, diagnostics);
        }
    }

    /// <summary>
    /// Analyzes a foreach loop. Moves inside the loop body without re-assignment
    /// emit <c>MIST_OWN_LOOP_MOVE</c>.
    /// </summary>
    private void AnalyzeForeachStatement(
        ForeachStatementNode foreachStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check collection expression for use-after-move.
        CheckExpressionForUseAfterMove(foreachStmt.Collection, state, model, diagnostics);

        // Snapshot which owned variables are Live before the loop body.
        var liveBeforeLoop = new Dictionary<ISymbol, VariableOwnershipState>();
        foreach(var (symbol, varState) in state.VariableStates)
        {
            if(varState == VariableOwnershipState.Live && IsOwnedVariable(symbol))
            {
                liveBeforeLoop[symbol] = varState;
            }
        }

        // Walk the loop body with isLoop=true.
        state.EnterScope();
        WalkBlock(foreachStmt.Body, state, model, diagnostics, isLoop: true);
        state.ExitScope();

        // After walking the body, any variable that was Live before and is now Moved
        // was moved without re-assignment in the same iteration → report MIST_OWN_LOOP_MOVE.
        ReportUnresolvedLoopMoves(liveBeforeLoop, state, foreachStmt, model, diagnostics);
    }

    /// <summary>
    /// Analyzes a return statement for use-after-move in the returned expression.
    /// </summary>
    private static void AnalyzeReturnStatement(
        ReturnStatementNode returnStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(returnStmt.Value is not null)
        {
            CheckExpressionForUseAfterMove(returnStmt.Value, state, model, diagnostics);

            // If returning an owned variable, it's a move out of the method.
            var sourceSymbol = TryResolveIdentifierSymbol(returnStmt.Value, model);
            if(sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
            {
                PerformMove(sourceSymbol, returnStmt.Value, state, model, diagnostics);
            }
        }
    }

    // ── Move operations ──────────────────────────────────────────────────

    /// <summary>
    /// Performs a move of an owned variable: marks it as Moved and records the
    /// move location. Checks the truth table to ensure the move is permitted in
    /// the current state. Loop move detection is handled by the calling loop
    /// analysis methods (<see cref="AnalyzeWhileStatement"/>, <see cref="AnalyzeForStatement"/>,
    /// <see cref="AnalyzeForeachStatement"/>) which compare state before and after the body.
    /// </summary>
    private static void PerformMove(
        ISymbol sourceSymbol,
        SyntaxNode moveNode,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check truth table: is Move permitted in the current state?
        if(state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
        {
            if(!IsActionPermitted(currentState, AccessKind.Move))
            {
                ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.Move, moveNode, model, diagnostics);
                return;
            }
        }

        state.VariableStates[sourceSymbol] = VariableOwnershipState.Moved;
        state.MoveLocations[sourceSymbol] = new Common.Source.SourceSpan(moveNode.Span.Start, moveNode.Span.Length);
    }

    /// <summary>
    /// After walking a loop body, reports <c>MIST_OWN_LOOP_MOVE</c> for any owned variable
    /// that was Live before the loop and is now Moved (meaning it was moved inside the loop
    /// without being re-assigned in the same iteration).
    /// </summary>
    private static void ReportUnresolvedLoopMoves(
        Dictionary<ISymbol, VariableOwnershipState> liveBeforeLoop,
        MethodFlowState state,
        SyntaxNode loopNode,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach(var (symbol, _) in liveBeforeLoop)
        {
            if(state.VariableStates.TryGetValue(symbol, out var afterState) &&
                afterState == VariableOwnershipState.Moved)
            {
                // The variable was moved inside the loop without re-assignment.
                diagnostics.Add(MakeDiagnostic(
                    LoopMove,
                    $"Owned variable '{symbol.Name}' is moved inside a loop. The variable must be re-assigned before the next iteration or the move must be removed.",
                    loopNode,
                    model));
            }
        }
    }

    // ── Use-after-move detection ─────────────────────────────────────────

    /// <summary>
    /// Checks an expression for use-after-move violations. Walks the expression
    /// tree looking for identifier references to moved owned variables.
    /// </summary>
    private static void CheckExpressionForUseAfterMove(
        ExpressionNode expr,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Lambda-form expressions are handled by the type checker's capture rules.
        if(expr.AsAssignmentOrNull() is { } assignment)
        {
            CheckAssignmentValueForUseAfterMove(assignment, state, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks an assignment node's value (RHS) for use-after-move.
    /// </summary>
    private static void CheckAssignmentValueForUseAfterMove(
        AssignmentNode assignment,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the target (which is the expression when Value is null).
        CheckLogicalOrForUseAfterMove(assignment.Target, state, model, diagnostics);

        // If there's a chained assignment, check it recursively. The RHS is now a
        // full ExpressionNode; a lambda RHS has no use-after-move effect here.
        if(assignment.Value is not null && assignment.Value.AsAssignmentOrNull() is { } rhsAssignment)
        {
            CheckAssignmentValueForUseAfterMove(rhsAssignment, state, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks a LogicalOrNode tree for use-after-move by walking down to identifiers.
    /// Uses the truth table to check Read permission for each accessed variable.
    /// </summary>
    private static void CheckLogicalOrForUseAfterMove(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check if this node is an identifier referencing a variable in a restricted state.
        if(node is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is not null && IsOwnedVariable(symbol))
            {
                if(state.VariableStates.TryGetValue(symbol, out var varState) &&
                    !IsActionPermitted(varState, AccessKind.Read))
                {
                    ReportTruthTableViolation(symbol, varState, AccessKind.Read, identifier, model, diagnostics);
                }
            }
        }

        // Recursively check children.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CheckLogicalOrForUseAfterMove(child, state, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks method calls in an expression for owned argument passing (moves).
    /// When an owned variable is passed to an owned parameter, it is moved.
    /// </summary>
    private void CheckMethodCallsForMoves(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Look for PostfixNode with method call suffixes.
        if(node is PostfixNode postfix)
        {
            // Check mutation blocking: if the receiver is in a restricted state,
            // use the truth table to check whether the access is permitted.
            if(postfix.Suffix.Length > 0 && postfix.Suffix[0] is MethodCallSuffixPostfixSuffixNode firstMethodCall)
            {
                var receiverSymbol = model.GetDeclaredSymbol(postfix.Target);

                // ── This-tier call-site checking (Req 17 AC 15–16) ─────────────
                // For every method call, verify the caller's binding tier satisfies the
                // method's inferred/declared this-tier. If the tier is insufficient,
                // emit MIST_OWN_THIS_TIER_MISMATCH. This replaces the old
                // MethodTierVisibilityFilter blanket hiding (Req 6).
                var consumedByWithOwned = false;
                if(receiverSymbol is not null)
                {
                    var receiverType = receiverSymbol.DeclaredType;
                    if(receiverType is not null)
                    {
                        var calledMethod = LookupMemberByName(receiverType, firstMethodCall.Name.Lexeme);
                        if(calledMethod is not null)
                        {
                            var requiredTier = ThisTierInferencePass.GetThisTier(calledMethod);
                            if(requiredTier != ThisTier.None)
                            {
                                // Determine the caller's effective tier from the receiver symbol.
                                var callerTier = ThisTier.None;
                                if(state.BindingKinds.TryGetValue(receiverSymbol, out var receiverBinding) &&
                                    receiverBinding == BindingKind.Owned)
                                {
                                    callerTier = ThisTier.Owned;
                                }
                                else if(state.InferredTiers.TryGetValue(receiverSymbol, out var inferredTier))
                                {
                                    callerTier = ThisTierExtensions.FromOwnershipTier(inferredTier);
                                }
                                else if(receiverSymbol is Symbol recvSym)
                                {
                                    callerTier = ThisTierExtensions.FromOwnershipTier(recvSym.OwnershipTier);
                                }

                                if(!ThisTierExtensions.Satisfies(callerTier, requiredTier))
                                {
                                    diagnostics.Add(MakeDiagnostic(
                                        ThisTierMismatch,
                                        ThisTierMismatchMessage(
                                            calledMethod.Name,
                                            ThisTierInferencePass.FormatTier(requiredTier),
                                            receiverSymbol.Name,
                                            ThisTierInferencePass.FormatTier(callerTier)),
                                        firstMethodCall,
                                        model));
                                }
                                else if(requiredTier == ThisTier.Owned)
                                {
                                    // 'with owned' method consumes the receiver (Req 17 AC 13):
                                    // the caller's binding is moved into the method and becomes
                                    // dead after the call. Transition receiver to Moved state.
                                    PerformMove(receiverSymbol, firstMethodCall, state, model, diagnostics);
                                    consumedByWithOwned = true;
                                }
                            }
                        }
                    }
                }

                if(!consumedByWithOwned && receiverSymbol is not null &&
                    state.VariableStates.TryGetValue(receiverSymbol, out var receiverState))
                {
                    if(receiverState == VariableOwnershipState.LentRef)
                    {
                        // LentRef: all access is blocked (Read is false in truth table).
                        if(!IsActionPermitted(receiverState, AccessKind.Read))
                        {
                            ReportTruthTableViolation(receiverSymbol, receiverState, AccessKind.Read, firstMethodCall, model, diagnostics);
                        }
                    }
                    else if(receiverState == VariableOwnershipState.LentFrozen)
                    {
                        // LentFrozen: check if the method is non-readonly (mutation attempt = Write).
                        var receiverType = receiverSymbol.DeclaredType;
                        if(receiverType is not null)
                        {
                            var methodSym = LookupMemberByName(receiverType, firstMethodCall.Name.Lexeme);
                            if(methodSym is not null && !IsReadonlyMember(methodSym))
                            {
                                if(!IsActionPermitted(receiverState, AccessKind.Write))
                                {
                                    diagnostics.Add(MakeDiagnostic(
                                        MutationBlocked,
                                        $"Cannot call non-readonly method '{methodSym.Name}' on variable '{receiverSymbol.Name}' " +
                                        $"while a frozen ref is outstanding. Mutation is blocked until all frozen refs are returned.",
                                        firstMethodCall,
                                        model));
                                }
                            }
                        }
                    }
                    else if(receiverState == VariableOwnershipState.Moved)
                    {
                        // Moved: all access is blocked.
                        if(!IsActionPermitted(receiverState, AccessKind.Read))
                        {
                            ReportTruthTableViolation(receiverSymbol, receiverState, AccessKind.Read, firstMethodCall, model, diagnostics);
                        }
                    }
                }

                // ── Per-field state check for method calls (Req 16 AC 23–24) ──────────
                // When the receiver has per-field state tracking active and at least one
                // field is non-Live, check whether the called method accesses any field
                // whose current state does not permit the required action. A method that
                // only touches Live fields is permitted.
                if(receiverSymbol is not null &&
                    state.FieldStates.TryGetValue(receiverSymbol, out var receiverFieldStates) &&
                    HasNonLiveField(receiverFieldStates))
                {
                    var receiverType = receiverSymbol.DeclaredType;
                    if(receiverType is not null)
                    {
                        var calledMethodSym = LookupMemberByName(receiverType, firstMethodCall.Name.Lexeme);
                        if(calledMethodSym is not null)
                        {
                            // Use ThisTierInferencePass to determine which owned fields the method accesses.
                            var accessedFields = ThisTierInferencePass.GetAccessedFields(calledMethodSym, model);

                            foreach(var (fieldSym, accessKind) in accessedFields)
                            {
                                var fieldState = state.GetFieldState(receiverSymbol, fieldSym);
                                if(fieldState is null or VariableOwnershipState.Live)
                                {
                                    continue;
                                }

                                // Map FieldAccessKind to AccessKind for truth table lookup.
                                var requiredAction = accessKind == FieldAccessKind.Write
                                    ? AccessKind.Write
                                    : AccessKind.Read;

                                if(!IsActionPermitted(fieldState.Value, requiredAction))
                                {
                                    ReportFieldStateViolation(
                                        receiverSymbol, fieldSym, fieldState.Value, requiredAction,
                                        firstMethodCall, model, diagnostics);
                                    break; // Report the first violation and stop.
                                }
                            }
                        }
                    }
                }
            }

            foreach(var suffix in postfix.Suffix)
            {
                if(suffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    CheckMethodCallArgumentsForMoves(methodCall, state, model, diagnostics);
                }
                else if(suffix is InvocationSuffixPostfixSuffixNode invocation)
                {
                    CheckInvocationArgumentsForMoves(invocation, postfix.Target, state, model, diagnostics);
                }
            }
        }

        // Check constructor calls (new T(...)) for owned parameter passing.
        if(node is ObjectCreationExpressionPrimaryNode objectCreation)
        {
            CheckConstructorArgumentsForMoves(objectCreation, state, model, diagnostics);
        }

        // Recursively check children.
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                CheckMethodCallsForMoves(child, state, model, diagnostics, isLoop);
            }
        }
    }

    /// <summary>
    /// Checks arguments of a method call for owned parameter passing (moves) and
    /// lending compatibility violations.
    /// </summary>
    private void CheckMethodCallArgumentsForMoves(
        MethodCallSuffixPostfixSuffixNode methodCall,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Resolve the method symbol to get parameter ownership tiers.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if(methodSymbol is null)
        {
            return;
        }

        // Get parameter symbols.
        SyntaxNode methodDeclNode;
        try { methodDeclNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        var paramSymbols = GetParameterSymbols(methodDeclNode, model);
        if(paramSymbols is null)
        {
            return;
        }

        // Get argument expressions.
        var argExprs = CollectArgumentExpressions(methodCall.Arguments);

        var count = Math.Min(paramSymbols.Count, argExprs.Count);
        for(var i = 0; i < count; i++)
        {
            if(paramSymbols[i] is Symbol paramSym)
            {
                // Skip ownership checks for ref parameters — they use a different
                // mechanism (variable-level indirection) that is orthogonal to ownership lending.
                if(paramSym.RefKind != RefKind.None)
                {
                    continue;
                }

                if(paramSym.OwnershipTier == OwnershipTier.Owned)
                {
                    // Owned parameter pass is an owned-move site (Req 6 AC 3, Req 9 AC 4):
                    // route the argument through the single move boundary. The resolver
                    // consumes an owned source (marking it Moved), copies value types,
                    // clones a non-owned clonable source, and emits MIST_OWN_REQUIRES_OWNED /
                    // MIST_OWN_NOT_CLONABLE for a non-owned, non-clonable source.
                    _ = resolver.Resolve(
                        argExprs[i],
                        paramSym.DeclaredType,

                        state,
                        model,
                        diagnostics);
                    // Skip lending compatibility — the resolver fully validates owned-parameter passing
                    // including implicit cloning for non-owned clonable sources (Req 6 AC 9).
                    continue;
                }

                // Check lending compatibility for non-owned tier combinations.
                CheckLendingCompatibility(argExprs[i], paramSym, state, model, diagnostics);
            }
        }

        // Check ref exclusivity: same variable passed to multiple ref parameters.
        CheckRefExclusivity(argExprs, paramSymbols, model, diagnostics);
    }

    /// <summary>
    /// Checks arguments of a direct invocation for owned parameter passing (moves) and
    /// lending compatibility violations.
    /// </summary>
    private void CheckInvocationArgumentsForMoves(
        InvocationSuffixPostfixSuffixNode invocation,
        PrimaryNode target,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Resolve the invoked symbol from the target identifier first,
        // then fall back to the invocation node itself.
        ISymbol? invokedSymbol = null;
        if(target is IdentifierExpressionPrimaryNode targetId)
        {
            invokedSymbol = model.GetDeclaredSymbol(targetId);
        }

        invokedSymbol ??= model.GetDeclaredSymbol(invocation);
        if(invokedSymbol is null)
        {
            return;
        }

        // Get parameter symbols.
        SyntaxNode declNode;
        try { declNode = model.GetDeclaringNode(invokedSymbol); }
        catch { return; }

        var paramSymbols = GetParameterSymbols(declNode, model);
        if(paramSymbols is null)
        {
            return;
        }

        // Get argument expressions.
        var argExprs = CollectArgumentExpressions(invocation.Arguments);

        var count = Math.Min(paramSymbols.Count, argExprs.Count);
        for(var i = 0; i < count; i++)
        {
            if(paramSymbols[i] is Symbol paramSym)
            {
                // Skip ownership checks for ref parameters — they use a different
                // mechanism (variable-level indirection) that is orthogonal to ownership lending.
                if(paramSym.RefKind != RefKind.None)
                {
                    continue;
                }

                if(paramSym.OwnershipTier == OwnershipTier.Owned)
                {
                    // Owned parameter pass is an owned-move site (Req 6 AC 3, Req 9 AC 4):
                    // route the argument through the single move boundary.
                    _ = resolver.Resolve(
                        argExprs[i],
                        paramSym.DeclaredType,

                        state,
                        model,
                        diagnostics);
                    // Skip lending compatibility — the resolver fully validates owned-parameter passing.
                    continue;
                }

                // Check lending compatibility for non-owned tier combinations.
                CheckLendingCompatibility(argExprs[i], paramSym, state, model, diagnostics);
            }
        }

        // Check ref exclusivity: same variable passed to multiple ref parameters.
        CheckRefExclusivity(argExprs, paramSymbols, model, diagnostics);
    }

    /// <summary>
    /// Checks arguments of a constructor call (<c>new T(...)</c>) for owned parameter passing
    /// (moves) and lending compatibility violations.
    /// </summary>
    private void CheckConstructorArgumentsForMoves(
        ObjectCreationExpressionPrimaryNode objectCreation,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(objectCreation.Arguments is null)
        {
            return;
        }

        // Resolve the type being constructed and find the matching constructor.
        var createdType = model.GetTypeOf(objectCreation);
        if(createdType is null)
        {
            return;
        }

        // Unwrap constructed types to get the definition.
        var resolvedType = createdType is ConstructedType ct ? ct.GenericDefinition : createdType;

        var memberScope = resolvedType switch
        {
            ClassType cct => cct.MemberScope,
            StructType sst => sst.MemberScope,
            _ => null,
        };

        if(memberScope is null)
        {
            return;
        }

        // Find constructor symbols.
        var argExprs = CollectArgumentExpressions(objectCreation.Arguments);
        var argCount = argExprs.Count;

        ISymbol? matchedCtor = null;
        foreach(var member in memberScope.Members)
        {
            if(member.Kind != SymbolKind.Constructor)
            {
                continue;
            }

            // Get the constructor's declaring node to count parameters.
            SyntaxNode ctorDeclNode;
            try { ctorDeclNode = model.GetDeclaringNode(member); }
            catch { continue; }

            var ctorParams = GetParameterSymbols(ctorDeclNode, model);
            if(ctorParams is not null && ctorParams.Count == argCount)
            {
                matchedCtor = member;
                break;
            }
        }

        if(matchedCtor is null)
        {
            return;
        }

        // Get parameter symbols from the matched constructor.
        SyntaxNode declNode;
        try { declNode = model.GetDeclaringNode(matchedCtor); }
        catch { return; }

        var paramSymbols = GetParameterSymbols(declNode, model);
        if(paramSymbols is null)
        {
            return;
        }

        var count = Math.Min(paramSymbols.Count, argExprs.Count);
        for(var i = 0; i < count; i++)
        {
            if(paramSymbols[i] is Symbol paramSym)
            {
                if(paramSym.RefKind != RefKind.None)
                {
                    continue;
                }

                if(paramSym.OwnershipTier == OwnershipTier.Owned)
                {
                    // Owned parameter pass: route through the move boundary.
                    _ = resolver.Resolve(
                        argExprs[i],
                        paramSym.DeclaredType,

                        state,
                        model,
                        diagnostics);
                    // Skip lending compatibility — the resolver fully validates owned-parameter passing.
                    continue;
                }

                // Check lending compatibility for non-owned tier combinations.
                CheckLendingCompatibility(argExprs[i], paramSym, state, model, diagnostics);
            }
        }
    }

    // ── Lending compatibility ────────────────────────────────────────────

    /// <summary>
    /// The set of permitted (source tier, target tier) lending pairs.
    /// </summary>
    private static readonly HashSet<(OwnershipTier Source, OwnershipTier Target)> PermittedLendingPairs = new()
    {
        (OwnershipTier.Owned, OwnershipTier.Owned),
        (OwnershipTier.Owned, OwnershipTier.Borrowed),
        (OwnershipTier.Owned, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Owned, OwnershipTier.Frozen),
        (OwnershipTier.Borrowed, OwnershipTier.Borrowed),
        (OwnershipTier.Borrowed, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Borrowed, OwnershipTier.Frozen),
        (OwnershipTier.ReadonlyRef, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Frozen, OwnershipTier.Frozen),
        (OwnershipTier.Frozen, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Unrestricted, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Unrestricted, OwnershipTier.Unrestricted),
    };

    /// <summary>
    /// Checks whether passing an argument to a parameter satisfies the lending
    /// compatibility matrix. Value types always satisfy any tier. For reference types,
    /// the (source tier, target tier) pair must be in the permitted set.
    /// Also uses BindingKind from MethodFlowState to reject unrestricted bindings
    /// passed to borrowed or frozen parameters.
    /// </summary>
    private static void CheckLendingCompatibility(
        SyntaxNode argExpr,
        Symbol paramSym,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var targetTier = paramSym.OwnershipTier;

        // Resolve the argument to a symbol to get its ownership tier.
        var argSymbol = TryResolveIdentifierSymbol(argExpr, model);
        if(argSymbol is not Symbol argSym)
        {
            return;
        }

        // Value types always satisfy any tier requirement.
        if(argSym.DeclaredType is not null && IsValueType(argSym.DeclaredType))
        {
            return;
        }

        var sourceTier = argSym.OwnershipTier;

        // Use BindingKind from MethodFlowState to determine if the argument is an
        // unrestricted binding. This provides flow-sensitive detection that covers
        // cases where the symbol's declared tier alone may not tell the full story.
        if(targetTier is OwnershipTier.Borrowed or OwnershipTier.Frozen &&
            state.BindingKinds.TryGetValue(argSymbol, out var argBindingKind) &&
            argBindingKind == BindingKind.Unrestricted)
        {
            diagnostics.Add(MakeDiagnostic(
                UnrestrictedToRef,
                $"Cannot pass unrestricted variable '{argSym.Name}' to {FormatOwnershipTier(targetTier)} parameter '{paramSym.Name}'. " +
                $"Unrestricted references cannot provide the guarantees required by {FormatOwnershipTier(targetTier)}.",
                argExpr,
                model));
            return;
        }

        // Check if the pair is permitted.
        if(PermittedLendingPairs.Contains((sourceTier, targetTier)))
        {
            return;
        }

        // Emit the appropriate diagnostic.
        if(sourceTier == OwnershipTier.Unrestricted &&
            targetTier is OwnershipTier.Borrowed or OwnershipTier.Frozen or OwnershipTier.Owned)
        {
            diagnostics.Add(MakeDiagnostic(
                UnrestrictedToRef,
                $"Cannot pass unrestricted variable '{argSym.Name}' to {FormatOwnershipTier(targetTier)} parameter '{paramSym.Name}'. " +
                $"Unrestricted references cannot provide the guarantees required by {FormatOwnershipTier(targetTier)}.",
                argExpr,
                model));
        }
        else
        {
            diagnostics.Add(MakeDiagnostic(
                IncompatibleLend,
                $"Cannot lend {FormatOwnershipTier(sourceTier)} variable '{argSym.Name}' to {FormatOwnershipTier(targetTier)} parameter '{paramSym.Name}'. " +
                $"The lending pair ({FormatOwnershipTier(sourceTier)}, {FormatOwnershipTier(targetTier)}) is not permitted.",
                argExpr,
                model));
        }
    }

    // ── Symbol resolution helpers ────────────────────────────────────────

    /// <summary>
    /// Attempts to resolve an expression to a single identifier symbol.
    /// Returns the symbol if the expression is a bare identifier referencing
    /// a local variable or parameter; null otherwise.
    /// </summary>
    private static ISymbol? TryResolveIdentifierSymbol(SyntaxNode node, ISemanticModel model)
    {
        // Unwrap expression chain: ExpressionNode → AssignmentNode → LogicalOrNode → ... → PostfixNode → PrimaryNode
        if(node is ExpressionNode expr)
        {
            return expr.AsAssignmentOrNull() is { } exprAssignment
                ? TryResolveIdentifierSymbol(exprAssignment, model)
                : null; // Lambda form is not a simple identifier reference.
        }

        if(node is AssignmentNode assignment)
        {
            if(assignment.Value is not null)
            {
                return null; // Chained assignment, not a simple identifier.
            }

            return TryResolveIdentifierSymbol(assignment.Target, model);
        }

        // Walk down the single-child chain to find a PostfixNode with no suffixes.
        if(node is PostfixNode postfix)
        {
            if(postfix.Suffix.Length > 0)
            {
                return null; // Has suffixes (member access, method call, etc.)
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

        // For intermediate nodes in the expression chain (LogicalOr, LogicalAnd,
        // Equality, Relational, Additive, Multiplicative, Unary), walk through
        // non-null children looking for a single meaningful child node.
        // These generated nodes may report null Right properties as children,
        // so we filter to non-null children only.
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

    /// <summary>
    /// Attempts to resolve the target of an assignment (LogicalOrNode) to a symbol.
    /// </summary>
    private static ISymbol? TryResolveAssignmentTarget(SyntaxNode target, ISemanticModel model)
    {
        return TryResolveIdentifierSymbol(target, model);
    }

    /// <summary>
    /// Attempts to resolve the target of an assignment to a field symbol.
    /// Unlike <see cref="TryResolveIdentifierSymbol"/>, this also returns field symbols.
    /// </summary>
    private static ISymbol? TryResolveFieldTarget(SyntaxNode target, ISemanticModel model)
    {
        // Unwrap expression chain to find the identifier.
        if(target is ExpressionNode expr)
        {
            return expr.AsAssignmentOrNull() is { } exprAssignment
                ? TryResolveFieldTarget(exprAssignment, model)
                : null; // Lambda form is not a field target.
        }

        if(target is AssignmentNode assignment)
        {
            if(assignment.Value is not null)
            {
                return null;
            }

            return TryResolveFieldTarget(assignment.Target, model);
        }

        if(target is PostfixNode postfix)
        {
            if(postfix.Suffix.Length > 0)
            {
                return null;
            }

            return TryResolveFieldTarget(postfix.Target, model);
        }

        if(target is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is not null && symbol.Kind == SymbolKind.Field)
            {
                return symbol;
            }

            return null;
        }

        // Walk single-child chains.
        var nonNullChildren = new List<SyntaxNode>();
        foreach(var child in target.Children())
        {
            if(child is not null)
            {
                nonNullChildren.Add(child);
            }
        }

        if(nonNullChildren.Count == 1)
        {
            return TryResolveFieldTarget(nonNullChildren[0], model);
        }

        return null;
    }

    /// <summary>
    /// Attempts to resolve the value side of an assignment to a symbol.
    /// </summary>
    private static ISymbol? TryResolveAssignmentValueSymbol(AssignmentNode value, ISemanticModel model)
    {
        if(value.Value is not null)
        {
            return null; // Chained assignment.
        }

        return TryResolveIdentifierSymbol(value.Target, model);
    }

    /// <summary>
    /// Collects argument expressions from an ArgumentListNode.
    /// </summary>
    private static List<SyntaxNode> CollectArgumentExpressions(ArgumentListNode? argList)
    {
        var result = new List<SyntaxNode>();
        if(argList is null)
        {
            return result;
        }

        // The ArgumentListNode has Argument (first) and Arguments (rest),
        // each wrapping an ExpressionNode.
        result.Add(ArgumentListNodeExtensions.ExtractExpression(argList.Argument));
        foreach(var restArg in argList.Arguments)
        {
            result.Add(ArgumentListNodeExtensions.ExtractExpression(restArg));
        }

        return result;
    }

    /// <summary>
    /// Returns <c>true</c> if the symbol is an owned variable (local variable or parameter
    /// with <see cref="OwnershipTier.Owned"/>).
    /// </summary>
    private static bool IsOwnedVariable(ISymbol symbol)
    {
        if(symbol is not Symbol sym)
        {
            return false;
        }

        if(sym.Kind is not (SymbolKind.LocalVariable or SymbolKind.Parameter))
        {
            return false;
        }

        return sym.OwnershipTier == OwnershipTier.Owned;
    }

    /// <summary>
    /// Looks up a member symbol by name in the given type's member scope.
    /// Returns null if not found.
    /// </summary>
    private static ISymbol? LookupMemberByName(IType type, string name)
    {
        var memberScope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        return memberScope?.Lookup(name);
    }

    /// <summary>
    /// Returns <c>true</c> if the given symbol is classified as readonly
    /// (either explicitly or inferred by the ReadonlyInferenceEngine).
    /// </summary>
    private static bool IsReadonlyMember(ISymbol symbol)
    {
        if(symbol is not Symbol sym)
        {
            return false;
        }

        return sym.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly or ReadonlyStatus.InferredReadonly;
    }

    /// <summary>
    /// Returns <c>true</c> if any field in the given per-field state map is in a non-Live state.
    /// Used to short-circuit the per-field method call check (Req 16 AC 23–24).
    /// </summary>
    private static bool HasNonLiveField(Dictionary<ISymbol, VariableOwnershipState> fieldStates)
    {
        foreach(var (_, fieldState) in fieldStates)
        {
            if(fieldState != VariableOwnershipState.Live)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks whether there is already an active ref borrow from the given lender.
    /// </summary>
    private static bool HasActiveRefBorrow(ISymbol lender, MethodFlowState state)
    {
        foreach(var borrow in state.ActiveBorrows)
        {
            if(ReferenceEquals(borrow.Lender, lender) && borrow.BorrowTier == OwnershipTier.Borrowed)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Attempts to resolve the receiver of a field access expression (e.g., <c>x.field</c>
    /// returns the symbol for <c>x</c>). Returns null if the target is not a field access
    /// or the receiver cannot be resolved.
    /// </summary>
    private static ISymbol? TryResolveFieldAccessReceiver(SyntaxNode target, ISemanticModel model)
    {
        // Unwrap the expression chain to find a PostfixNode with a MemberAccessSuffix.
        var nonNullChildren = new List<SyntaxNode>();
        var current = target;
        while(current is not PostfixNode)
        {
            nonNullChildren.Clear();
            foreach(var child in current.Children())
            {
                if(child is not null)
                {
                    nonNullChildren.Add(child);
                }
            }

            if(nonNullChildren.Count != 1)
            {
                return null;
            }

            current = nonNullChildren[0];
        }

        if(current is PostfixNode postfix && postfix.Suffix.Length > 0 &&
            postfix.Suffix[^1] is MemberAccessSuffixPostfixSuffixNode)
        {
            // The receiver is the PostfixNode's target (the identifier before the dot).
            if(postfix.Target is IdentifierExpressionPrimaryNode receiverId)
            {
                var symbol = model.GetDeclaredSymbol(receiverId);
                if(symbol is not null &&
                    symbol.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
                {
                    return symbol;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to resolve a <c>this.field</c> assignment target to the field symbol.
    /// Returns the field symbol if the target is a <c>this.field</c> pattern (PostfixNode
    /// with <see cref="ThisExpressionPrimaryNode"/> target and a single MemberAccessSuffix),
    /// otherwise returns null.
    /// </summary>
    private static ISymbol? TryResolveThisFieldTarget(SyntaxNode target, ISemanticModel model)
    {
        // Unwrap expression chain to find a PostfixNode.
        var nonNullChildren = new List<SyntaxNode>();
        var current = target;
        while(current is not PostfixNode)
        {
            nonNullChildren.Clear();
            foreach(var child in current.Children())
            {
                if(child is not null)
                {
                    nonNullChildren.Add(child);
                }
            }

            if(nonNullChildren.Count != 1)
            {
                return null;
            }

            current = nonNullChildren[0];
        }

        if(current is PostfixNode postfix &&
            postfix.Suffix.Length == 1 &&
            postfix.Target is ThisExpressionPrimaryNode &&
            postfix.Suffix[0] is MemberAccessSuffixPostfixSuffixNode memberAccess)
        {
            var receiverType = model.GetTypeOf(postfix.Target);
            if(receiverType is null)
            {
                return null;
            }

            var member = LookupMemberByName(receiverType, memberAccess.Name.Lexeme);
            if(member is not null && member.Kind == SymbolKind.Field)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Attempts to resolve the field symbol from a member access expression (e.g., <c>x.field</c>
    /// returns the field symbol for <c>field</c>). Uses the receiver's declared type to look up
    /// the member by name from the <see cref="MemberAccessSuffixPostfixSuffixNode"/>.
    /// Returns null if the expression is not a field access or the field cannot be resolved.
    /// </summary>
    private static ISymbol? TryResolveFieldAccessField(SyntaxNode target, ISemanticModel model)
    {
        // Unwrap the expression chain to find a PostfixNode with a MemberAccessSuffix.
        var nonNullChildren = new List<SyntaxNode>();
        var current = target;
        while(current is not PostfixNode)
        {
            nonNullChildren.Clear();
            foreach(var child in current.Children())
            {
                if(child is not null)
                {
                    nonNullChildren.Add(child);
                }
            }

            if(nonNullChildren.Count != 1)
            {
                return null;
            }

            current = nonNullChildren[0];
        }

        if(current is PostfixNode postfix && postfix.Suffix.Length > 0 &&
            postfix.Suffix[^1] is MemberAccessSuffixPostfixSuffixNode memberAccess)
        {
            // The receiver is the PostfixNode's target (the identifier before the dot).
            if(postfix.Target is IdentifierExpressionPrimaryNode receiverId)
            {
                var receiverSymbol = model.GetDeclaredSymbol(receiverId);
                if(receiverSymbol is null)
                {
                    return null;
                }

                var receiverType = receiverSymbol.DeclaredType;
                if(receiverType is null)
                {
                    return null;
                }

                // Unwrap ConstructedType to get the generic definition for member lookup.
                var lookupType = receiverType is ConstructedType constructed
                    ? constructed.GenericDefinition
                    : receiverType;

                // Look up the field by name in the receiver's type.
                var fieldName = memberAccess.Name.Lexeme;
                return LookupMemberByName(lookupType, fieldName);
            }
        }

        return null;
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <c>true</c> if the given type is a value type (struct or enum).
    /// </summary>
    private static bool IsValueType(IType type)
    {
        return type.Kind is TypeKind.Struct or TypeKind.Enum;
    }

    /// <summary>
    /// Returns <c>true</c> if the given type is declared as an owned type.
    /// </summary>
    private static bool IsOwnedType(IType type)
    {
        // For constructed generic types (e.g., LinearBox<int>), check the generic definition.
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false
        };
    }

    /// <summary>
    /// Returns <c>true</c> if the given type implements <c>IClonable&lt;T&gt;</c>, consulting
    /// the <c>ImplementsIClonable</c> flag cached on the type model by the IClonable derivation
    /// engine (Req 2 AC 3, Req 2 AC 6).
    /// </summary>
    private static bool ImplementsIClonable(IType type)
    {
        var resolved = type is ConstructedType constructedType ? constructedType.GenericDefinition : type;
        return resolved switch
        {
            ClassType ct => ct.ImplementsIClonable,
            StructType st => st.ImplementsIClonable,
            _ => false
        };
    }

    /// <summary>
    /// Checks whether a modifier list contains the <c>owned</c> modifier.
    /// </summary>
    private static bool HasOwnedModifier(System.Collections.Immutable.ImmutableArray<ModifierNode> modifiers)
    {
        foreach(var modifier in modifiers)
        {
            if(modifier is OwnedModifierModifierNode)
            {
                return true;
            }
        }

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 3: Ownership signature matching validation (Task 4.4)
    // Requirements: 9.1, 9.2, 9.3, 9.4, 2.5
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates ownership signature matching for abstract type implementations.
    /// Also rejects non-owned specifiers on return types.
    /// </summary>
    private static void ValidateSignatureMatching(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // 1. Check invalid return type specifiers on all method declarations.
        ValidateReturnTypeSpecifiers(model, diagnostics);

        // 2. Check abstract type method implementation ownership matching.
        ValidateInterfaceOwnershipMatching(model, diagnostics);
    }

    /// <summary>
    /// Rejects non-owned ownership specifiers on method return types.
    /// Only <c>owned</c> is valid as a return type specifier; <c>ref</c>,
    /// <c>readonly ref</c>, and <c>frozen ref</c> are invalid (Requirement 2.5).
    /// </summary>
    private static void ValidateReturnTypeSpecifiers(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind != SymbolKind.Method)
            {
                continue;
            }

            if(symbol is not Symbol methodSymbol)
            {
                continue;
            }

            // Only non-owned specifiers on return types are invalid.
            if(methodSymbol.OwnershipTier is OwnershipTier.Borrowed
                or OwnershipTier.ReadonlyRef
                or OwnershipTier.Frozen)
            {
                SyntaxNode declaringNode;
                try { declaringNode = model.GetDeclaringNode(symbol); }
                catch { continue; }

                var tierName = FormatOwnershipTier(methodSymbol.OwnershipTier);
                diagnostics.Add(MakeDiagnostic(
                    InvalidReturnSpec,
                    $"'{tierName}' is not valid on a return type. Only 'owned' is permitted as a return type ownership specifier.",
                    declaringNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Checks that abstract type method implementations match ownership specifiers
    /// on parameters and return types exactly (Requirements 9.1, 9.2).
    /// </summary>
    private static void ValidateInterfaceOwnershipMatching(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin == SymbolOrigin.Backend)
            {
                continue;
            }

            if(symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
            {
                continue;
            }

            var type = symbol.DeclaredType;
            var abstracts = type switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                _ => null,
            };

            if(abstracts is null || abstracts.Count == 0)
            {
                continue;
            }

            var memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if(memberScope is null)
            {
                continue;
            }

            var visited = new HashSet<AbstractType>(ReferenceEqualityComparer.Instance);
            CheckAbstractOwnershipContracts(symbol, abstracts, memberScope, model, diagnostics, visited);
        }
    }

    /// <summary>
    /// Recursively checks ownership specifier matching for all abstract type methods.
    /// </summary>
    private static void CheckAbstractOwnershipContracts(
        ISymbol typeSymbol,
        IReadOnlyList<IType> abstracts,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        HashSet<AbstractType> visited)
    {
        foreach(var abstractType in abstracts)
        {
            if(abstractType is not AbstractType abs)
            {
                continue;
            }

            if(!visited.Add(abs))
            {
                continue;
            }

            foreach(var absMember in abs.MemberScope.Members)
            {
                if(absMember.Kind != SymbolKind.Method)
                {
                    continue;
                }

                // Find the implementing method in the type's member scope.
                var implMember = FindMethodInScope(absMember.Name, memberScope);
                if(implMember is null)
                {
                    continue; // Missing implementation is reported by the type checker.
                }

                CheckMethodOwnershipMatch(
                    typeSymbol, absMember, implMember, abs.DisplayName, model, diagnostics);
            }

            // Recurse into parent abstract types.
            if(abs.Abstracts.Count > 0)
            {
                CheckAbstractOwnershipContracts(typeSymbol, abs.Abstracts, memberScope, model, diagnostics, visited);
            }
        }
    }

    /// <summary>
    /// Compares ownership specifiers on parameters and return type between an
    /// abstract type method and its implementation. Emits MIST_OWN_SIGNATURE_MISMATCH
    /// for any mismatch (Requirements 9.1, 9.2, 9.3, 9.4).
    /// </summary>
    private static void CheckMethodOwnershipMatch(
        ISymbol typeSymbol,
        ISymbol contractMethod,
        ISymbol implMethod,
        string contractName,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Get declaring nodes to access parameter lists.
        SyntaxNode contractNode;
        SyntaxNode implNode;
        try
        {
            contractNode = model.GetDeclaringNode(contractMethod);
            implNode = model.GetDeclaringNode(implMethod);
        }
        catch
        {
            return;
        }

        // Check return type ownership match.
        if(implMethod is Symbol implSym && contractMethod is Symbol contractSym)
        {
            if(implSym.OwnershipTier != contractSym.OwnershipTier)
            {
                var expectedTier = FormatOwnershipTier(contractSym.OwnershipTier);
                var actualTier = FormatOwnershipTier(implSym.OwnershipTier);
                diagnostics.Add(MakeDiagnostic(
                    SignatureMismatch,
                    $"Return type ownership mismatch: '{typeSymbol.Name}.{implMethod.Name}' has '{actualTier}' but '{contractName}.{contractMethod.Name}' requires '{expectedTier}'.",
                    implNode,
                    model));
            }
        }

        // Check parameter ownership match.
        var contractParams = GetParameterSymbols(contractNode, model);
        var implParams = GetParameterSymbols(implNode, model);

        if(contractParams is null || implParams is null)
        {
            return;
        }

        var count = Math.Min(contractParams.Count, implParams.Count);
        for(var i = 0; i < count; i++)
        {
            if(contractParams[i] is not Symbol contractParam || implParams[i] is not Symbol implParam)
            {
                continue;
            }

            if(contractParam.OwnershipTier != implParam.OwnershipTier)
            {
                var expectedTier = FormatOwnershipTier(contractParam.OwnershipTier);
                var actualTier = FormatOwnershipTier(implParam.OwnershipTier);
                diagnostics.Add(MakeDiagnostic(
                    SignatureMismatch,
                    $"Parameter ownership mismatch: parameter '{implParam.Name}' in '{typeSymbol.Name}.{implMethod.Name}' has '{actualTier}' but '{contractName}.{contractMethod.Name}' requires '{expectedTier}'.",
                    implNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Extracts parameter symbols from a method declaration node.
    /// </summary>
    private static IReadOnlyList<ISymbol>? GetParameterSymbols(SyntaxNode methodNode, ISemanticModel model)
    {
        // Get the parameter list node from the method declaration.
        var paramList = methodNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            AbstractMethodDeclarationNode im => im.Parameters,
            ConstructorDeclarationNode c => c.Parameters,
            _ => null,
        };

        if(paramList is null)
        {
            return null;
        }

        var result = new List<ISymbol>();
        foreach(var child in paramList.Children())
        {
            if(child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if(paramSymbol is not null)
                {
                    result.Add(paramSymbol);
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Finds a method by name in a scope.
    /// </summary>
    private static ISymbol? FindMethodInScope(string name, IScope scope)
    {
        foreach(var member in scope.Members)
        {
            if(member.Name == name && member.Kind == SymbolKind.Method)
            {
                return member;
            }
        }

        return null;
    }

    /// <summary>
    /// Formats an <see cref="OwnershipTier"/> value as a human-readable string.
    /// </summary>
    private static string FormatOwnershipTier(OwnershipTier tier)
    {
        return tier switch
        {
            OwnershipTier.Owned => "owned",
            OwnershipTier.Borrowed => "borrowed",
            OwnershipTier.ReadonlyRef => "readonly ref",
            OwnershipTier.Frozen => "frozen",
            OwnershipTier.Unrestricted => "unrestricted",
            _ => "unrestricted",
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

    // ── Non-storability enforcement (Task 6.6) ───────────────────────────

    /// <summary>
    /// Validates that ref, readonly ref, and frozen ref variables are not stored
    /// in fields, collections, or returned from methods.
    /// </summary>
    private static void ValidateNonStorability(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach(var symbol in model.AllSymbols)
        {
            if(symbol.Origin != SymbolOrigin.UserCode)
            {
                continue;
            }

            // Only check field declarations.
            if(symbol.Kind != SymbolKind.Field)
            {
                continue;
            }

            if(symbol is not Symbol fieldSymbol)
            {
                continue;
            }

            // If the field has a non-storable ownership tier, emit MIST_OWN_NOT_STORABLE.
            if(fieldSymbol.OwnershipTier is OwnershipTier.Borrowed
                or OwnershipTier.ReadonlyRef
                or OwnershipTier.Frozen)
            {
                SyntaxNode declaringNode;
                try { declaringNode = model.GetDeclaringNode(symbol); }
                catch { continue; }

                var tierName = FormatOwnershipTier(fieldSymbol.OwnershipTier);
                diagnostics.Add(MakeDiagnostic(
                    NotStorable,
                    $"Field '{symbol.Name}' cannot be declared as '{tierName}' because ref-tier references are non-storable",
                    declaringNode,
                    model));
            }
        }
    }

    // ── Ref exclusivity enforcement (Task 6.10) ──────────────────────────

    /// <summary>
    /// Checks that the same variable is not passed to multiple ref parameters
    /// in a single method call (exclusivity violation).
    /// </summary>
    private static void CheckRefExclusivity(
        List<SyntaxNode> argExprs,
        IReadOnlyList<ISymbol> paramSymbols,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Track which symbols have been passed to ref parameters in this call.
        var refLentSymbols = new HashSet<ISymbol>(ReferenceEqualityComparer.Instance);

        var count = Math.Min(argExprs.Count, paramSymbols.Count);
        for(var i = 0; i < count; i++)
        {
            if(paramSymbols[i] is Symbol paramSym && paramSym.OwnershipTier == OwnershipTier.Borrowed)
            {
                var argSymbol = TryResolveIdentifierSymbol(argExprs[i], model);
                if(argSymbol is not null)
                {
                    if(!refLentSymbols.Add(argSymbol))
                    {
                        // Same variable passed to multiple ref parameters — exclusivity violation.
                        diagnostics.Add(MakeDiagnostic(
                            ExclusiveViolation,
                            $"Cannot lend variable '{argSymbol.Name}' as 'ref' to multiple parameters in the same call. " +
                            $"Only one mutable borrow is permitted at a time.",
                            argExprs[i],
                            model));
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════════
    // Owned field access helpers (Req 2, Req 16)
    // ═══════════════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Returns <c>true</c> if <paramref name="node"/> is a field access on an owned field
    /// through an owned parent binding. If true, outputs the field and receiver symbols for
    /// per-field state tracking.
    /// </summary>
    private static bool IsOwnedFieldAccessOnOwnedParent(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        out ISymbol? fieldSymbol,
        out ISymbol? receiverSymbol)
    {
        fieldSymbol = null;
        receiverSymbol = null;

        var receiver = TryResolveFieldAccessReceiver(node, model);
        if(receiver is null)
        {
            return false;
        }

        // Try both resolution strategies: TryResolveFieldTarget works for assignment
        // targets (LHS); TryResolveFieldAccessField works for read expressions (RHS/initializers).
        var field = TryResolveFieldTarget(node, model) ?? TryResolveFieldAccessField(node, model);
        if(field is null || field.Kind != SymbolKind.Field)
        {
            return false;
        }

        if(field is not Symbol fieldSym || !fieldSym.IsOwnedField)
        {
            return false;
        }

        if(!state.BindingKinds.TryGetValue(receiver, out var parentKind) || parentKind != BindingKind.Owned)
        {
            return false;
        }

        fieldSymbol = field;
        receiverSymbol = receiver;
        return true;
    }

    /// <summary>
    /// Returns <c>true</c> if <paramref name="node"/> is a field access on an owned field
    /// through a shared (non-owned) parent binding. If true, outputs the field and receiver
    /// symbols for diagnostics.
    /// </summary>
    private static bool IsOwnedFieldAccessOnSharedParent(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        out ISymbol? fieldSymbol,
        out ISymbol? receiverSymbol)
    {
        fieldSymbol = null;
        receiverSymbol = null;

        var receiver = TryResolveFieldAccessReceiver(node, model);
        if(receiver is null)
        {
            return false;
        }

        // Try both resolution strategies: TryResolveFieldTarget works for assignment
        // targets (LHS); TryResolveFieldAccessField works for read expressions (RHS/initializers).
        var field = TryResolveFieldTarget(node, model) ?? TryResolveFieldAccessField(node, model);
        if(field is null || field.Kind != SymbolKind.Field)
        {
            return false;
        }

        if(field is not Symbol fieldSym || !fieldSym.IsOwnedField)
        {
            return false;
        }

        // The parent is "shared" if it's not an owned binding (unrestricted or not tracked).
        if(state.BindingKinds.TryGetValue(receiver, out var parentKind) && parentKind == BindingKind.Owned)
        {
            return false;
        }

        fieldSymbol = field;
        receiverSymbol = receiver;
        return true;
    }

    /// <summary>
    /// Reports a diagnostic when a field operation is blocked by the field's per-field state.
    /// </summary>
    private static void ReportFieldStateViolation(
        ISymbol receiverSymbol,
        ISymbol fieldSymbol,
        VariableOwnershipState fieldState,
        AccessKind action,
        SyntaxNode node,

        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        var fieldName = $"{receiverSymbol.Name}.{fieldSymbol.Name}";
        var actionName = action switch
        {
            AccessKind.Move => "move out of",
            AccessKind.Borrow => "borrow",
            AccessKind.Freeze => "freeze",
            AccessKind.LiveView => "take a readonly ref of",
            AccessKind.Write => "write to",
            AccessKind.Read => "read",
            _ => "access"
        };

        if(fieldState == VariableOwnershipState.Moved)
        {
            diagnostics.Add(MakeDiagnostic(
                UseAfterMove,
                $"Cannot {actionName} field '{fieldName}' — the field has been moved out. " +
                $"Assign a new owned value to restore it before use.",
                node,
                model));
        }
        else if(fieldState == VariableOwnershipState.LentRef)
        {
            diagnostics.Add(MakeDiagnostic(
                ExclusiveViolation,
                $"Cannot {actionName} field '{fieldName}' — the field is currently borrowed (lent as a mutable reference).",
                node,
                model));
        }
        else if(fieldState == VariableOwnershipState.LentFrozen)
        {
            diagnostics.Add(MakeDiagnostic(
                MutationBlocked,
                $"Cannot {actionName} field '{fieldName}' — the field is currently frozen (a frozen reference is outstanding).",
                node,
                model));
        }
        else if(fieldState == VariableOwnershipState.LentReadonly)
        {
            diagnostics.Add(MakeDiagnostic(
                MutationBlocked,
                $"Cannot {actionName} field '{fieldName}' — the field has an outstanding readonly reference that prevents this operation.",
                node,
                model));
        }
    }
}