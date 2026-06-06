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

    /// <summary>Reference-type field in owned type is not owned.</summary>
    internal static readonly DiagnosticId FieldNotOwned = new("MIST_OWN_FIELD_NOT_OWNED");

    /// <summary>Owned binding declared with a type that is not an owned type (repurposed for Pass 2b).</summary>
    internal static readonly DiagnosticId TypeRequiresOwned = new("MIST_OWN_TYPE_REQUIRES_OWNED");

    /// <summary>Owned type attempts to implement interface/trait.</summary>
    internal static readonly DiagnosticId InterfaceForbidden = new("MIST_OWN_INTERFACE_FORBIDDEN");

    /// <summary>Ownership specifier mismatch in interface/trait satisfaction.</summary>
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
        if (TruthTable.TryGetValue((state, action), out var permitted))
            return permitted;

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
        MethodFlowState flowState,
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
        // Pass 1: Owned type declaration validation.
        ValidateOwnedTypeDeclarations(model, diagnostics);

        // Pass 2b: Owned binding type validation.
        ValidateOwnedBindingTypes(model, diagnostics);

        // Pass 2: Non-storability enforcement on declarations.
        ValidateNonStorability(model, diagnostics);

        // Pass 2c: Owned field modifier validation.
        ValidateOwnedFieldModifiers(model, diagnostics);

        // Pass 3: Ownership signature matching validation.
        ValidateSignatureMatching(model, diagnostics);

        // Pass 4: Flow-sensitive analysis (move tracking, borrow checking, etc.).
        AnalyzeFlowSensitive(model, diagnostics);
    }

    // ── Pass 1: Owned type declaration validation ────────────────────────

    /// <summary>
    /// Validates owned type declarations:
    /// - All reference-type fields in an owned type must be owned or of an owned type.
    /// - Owned types must not implement interfaces or satisfy traits.
    /// - The <c>owned</c> modifier is rejected on interface/trait declarations.
    /// </summary>
    private static void ValidateOwnedTypeDeclarations(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            switch (symbol.Kind)
            {
                case SymbolKind.Class:
                case SymbolKind.Struct:
                    ValidateOwnedClassOrStruct(symbol, model, diagnostics);
                    break;

                case SymbolKind.Interface:
                    RejectOwnedOnInterface(symbol, model, diagnostics);
                    break;

                case SymbolKind.Trait:
                    RejectOwnedOnTrait(symbol, model, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// For a class or struct declared as owned, validates:
    /// 1. All reference-type fields are owned or of an owned type.
    /// 2. The type does not implement any interfaces.
    /// 3. The type does not satisfy any traits.
    /// </summary>
    private static void ValidateOwnedClassOrStruct(ISymbol symbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var declaredType = symbol.DeclaredType;
        if (declaredType is null)
            return;

        var isOwnedType = declaredType switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false
        };

        if (!isOwnedType)
            return;

        // Get the declaring node for diagnostic location.
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        // Check 1: All reference-type fields must be owned or of an owned type.
        var memberScope = declaredType switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null
        };

        if (memberScope is not null)
        {
            foreach (var member in memberScope.Members)
            {
                if (member.Kind != SymbolKind.Field)
                    continue;

                var fieldType = member.DeclaredType;
                if (fieldType is null)
                    continue;

                // Value types are always permitted (Requirement 1.5).
                if (IsValueType(fieldType))
                    continue;

                // Type parameters are exempt — we cannot know at the generic definition
                // site whether T will be instantiated with a value type, an owned type,
                // or a non-owned reference type. Constraint checking at instantiation
                // sites is the correct enforcement point.
                if (fieldType is TypeParameterType)
                    continue;

                // Reject non-storable reference tiers on fields in owned types (Requirement 13.6).
                if (member is Symbol nonStorableSym &&
                    nonStorableSym.OwnershipTier is OwnershipTier.ReadonlyRef or OwnershipTier.Frozen or OwnershipTier.Borrowed)
                {
                    SyntaxNode fieldNode;
                    try { fieldNode = model.GetDeclaringNode(member); }
                    catch { fieldNode = declaringNode; }

                    var tierName = FormatOwnershipTier(nonStorableSym.OwnershipTier);
                    diagnostics.Add(MakeDiagnostic(
                        NotStorable,
                        $"Field '{member.Name}' in owned type '{symbol.Name}' cannot be declared as '{tierName}' because non-storable reference tiers cannot appear as fields in an owned type",
                        fieldNode,
                        model));
                    continue;
                }

                // Reference-type field: must be owned or of an owned type (Requirement 1.3).
                if (member is Symbol fieldSymbol && fieldSymbol.OwnershipTier == OwnershipTier.Owned)
                    continue;

                if (IsOwnedType(fieldType))
                    continue;

                // Emit MIST_OWN_FIELD_NOT_OWNED (Requirement 1.4).
                {
                    SyntaxNode fieldNode;
                    try { fieldNode = model.GetDeclaringNode(member); }
                    catch { fieldNode = declaringNode; }

                    diagnostics.Add(MakeDiagnostic(
                        FieldNotOwned,
                        $"Reference-type field '{member.Name}' in owned type '{symbol.Name}' must be declared as 'owned' or be of an owned type",
                        fieldNode,
                        model));
                }
            }
        }

        // Check 2: Owned types must not implement interfaces.
        var interfaces = declaredType switch
        {
            ClassType ct => ct.Interfaces,
            StructType st => st.Interfaces,
            _ => Array.Empty<IType>()
        };

        if (interfaces.Count > 0)
        {
            diagnostics.Add(MakeDiagnostic(
                InterfaceForbidden,
                $"Owned type '{symbol.Name}' cannot implement interfaces",
                declaringNode,
                model));
        }

        // Check 3: Owned types must not satisfy traits.
        if (declaredType is ClassType classType && classType.SatisfiedTraits.Count > 0)
        {
            diagnostics.Add(MakeDiagnostic(
                InterfaceForbidden,
                $"Owned type '{symbol.Name}' cannot satisfy traits",
                declaringNode,
                model));
        }
        else if (declaredType is StructType structType && structType.SatisfiedTraits.Count > 0)
        {
            diagnostics.Add(MakeDiagnostic(
                InterfaceForbidden,
                $"Owned type '{symbol.Name}' cannot satisfy traits",
                declaringNode,
                model));
        }
    }

    /// <summary>
    /// Rejects the <c>owned</c> modifier on interface declarations.
    /// </summary>
    private static void RejectOwnedOnInterface(ISymbol symbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        if (declaringNode is not InterfaceDeclarationNode interfaceDecl)
            return;

        if (!HasOwnedModifier(interfaceDecl.Modifiers))
            return;

        diagnostics.Add(MakeDiagnostic(
            InterfaceForbidden,
            $"The 'owned' modifier cannot be applied to interface '{symbol.Name}'",
            declaringNode,
            model));
    }

    /// <summary>
    /// Rejects the <c>owned</c> modifier on trait declarations.
    /// </summary>
    private static void RejectOwnedOnTrait(ISymbol symbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(symbol); }
        catch { return; }

        if (declaringNode is not TraitDeclarationNode traitDecl)
            return;

        if (!HasOwnedModifier(traitDecl.Modifiers))
            return;

        diagnostics.Add(MakeDiagnostic(
            InterfaceForbidden,
            $"The 'owned' modifier cannot be applied to trait '{symbol.Name}'",
            declaringNode,
            model));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 2b: Owned binding type validation (Task 6.1)
    // Requirements: 4.3, 4.4, 4.5, 4.6
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates owned binding declarations:
    /// - The binding's type must be an owned type (report MIST_OWN_TYPE_REQUIRES_OWNED if not).
    /// - The initializer must yield unique ownership: constructor call (new), call to an
    ///   owned-returning function, or move from another owned binding.
    /// </summary>
    private static void ValidateOwnedBindingTypes(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            if (symbol.Kind != SymbolKind.LocalVariable)
                continue;

            if (symbol is not Symbol varSymbol)
                continue;

            // Only check variables declared with the 'owned' specifier.
            if (varSymbol.OwnershipTier != OwnershipTier.Owned)
                continue;

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            // Check 1: The variable's type must be an owned type.
            var varType = varSymbol.DeclaredType;
            if (varType is not null && !IsValueType(varType) && !IsOwnedType(varType) && !SatisfiesOwnedTypeFieldRestriction(varType))
            {
                diagnostics.Add(MakeDiagnostic(
                    TypeRequiresOwned,
                    $"Variable '{symbol.Name}' declared as 'owned' requires a type declared with the 'owned' modifier, " +
                    $"but '{varType.DisplayName}' is not an owned type",
                    declaringNode,
                    model));
            }

            // Check 2: The initializer must yield unique ownership.
            var initializer = GetInitializerExpression(declaringNode);
            if (initializer is not null && !YieldsUniqueOwnership(initializer, model))
            {
                diagnostics.Add(MakeDiagnostic(
                    TypeRequiresOwned,
                    $"Owned binding '{symbol.Name}' requires a uniquely-owned value from a constructor call, " +
                    $"an owned-returning function, or a move from an existing owned binding",
                    declaringNode,
                    model));
            }
        }
    }

    /// <summary>
    /// Extracts the initializer expression from a variable declaration node.
    /// Returns null if the declaration has no initializer.
    /// </summary>
    private static SyntaxNode? GetInitializerExpression(SyntaxNode declaringNode)
    {
        return declaringNode switch
        {
            TypedVariableDeclarationVariableDeclarationStatementNode typed => typed.Initializer,
            InferredVariableDeclarationVariableDeclarationStatementNode inferred => inferred.Initializer,
            _ => null,
        };
    }

    /// <summary>
    /// Determines whether an initializer expression yields unique ownership.
    /// Valid sources of unique ownership:
    /// - Constructor call (new T())
    /// - Call to a function/method that returns an owned value
    /// - Move from another owned binding (identifier referencing an owned variable)
    /// </summary>
    private static bool YieldsUniqueOwnership(SyntaxNode expr, ISemanticModel model)
    {
        // Case 1: Check if it's an identifier referencing a local/parameter binding.
        // An identifier referencing a local/parameter yields unique ownership
        // only when the source binding is itself owned (move semantics).
        // A non-owned binding never yields unique ownership, even if the
        // underlying type is an owned type.
        var referencedSymbol = TryResolveIdentifierSymbol(expr, model);
        if (referencedSymbol is Symbol refSym &&
            refSym.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
        {
            return refSym.OwnershipTier == OwnershipTier.Owned;
        }

        // Case 2: Check if the top-level expression is a constructor call (new T())
        // or a function/method call returning owned.
        if (IsConstructorOrOwnedCall(expr, model))
            return true;

        // Case 3: For non-identifier, non-field-access expressions whose resolved type
        // is an owned type — the value must have come from a valid ownership source
        // (e.g. a method call that GetDeclaredSymbol couldn't resolve, cast, etc.).
        // Exclude member-access expressions (field reads) since accessing a field
        // does not transfer ownership.
        if (referencedSymbol is null && !IsMemberAccessExpression(expr))
        {
            var exprType = model.GetTypeOf(expr);
            if (exprType is not null && IsOwnedType(exprType))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns true if the expression is a member-access (field/property read) expression,
    /// which does not yield unique ownership.
    /// </summary>
    private static bool IsMemberAccessExpression(SyntaxNode node)
    {
        // Walk through wrapper nodes to find the postfix expression.
        if (node is PostfixNode postfix && postfix.Suffix.Length > 0)
        {
            var lastSuffix = postfix.Suffix[^1];
            return lastSuffix is MemberAccessSuffixPostfixSuffixNode
                or OptionalMemberAccessSuffixPostfixSuffixNode;
        }

        // Recurse through single-child wrappers.
        SyntaxNode? singleChild = null;
        var childCount = 0;
        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                childCount++;
                singleChild = child;
                if (childCount > 1)
                    break;
            }
        }

        if (childCount == 1 && singleChild is not null)
            return IsMemberAccessExpression(singleChild);

        return false;
    }

    /// <summary>
    /// Checks whether an expression is a constructor call (new T()) or a function/method
    /// call that returns an owned value. Walks through expression wrappers to find the
    /// primary expression.
    /// </summary>
    private static bool IsConstructorOrOwnedCall(SyntaxNode node, ISemanticModel model)
    {
        // Direct match on primary nodes.
        if (node is ObjectCreationExpressionPrimaryNode)
            return true;

        // PostfixNode with suffixes — check for method call or invocation returning owned.
        if (node is PostfixNode postfix)
        {
            if (postfix.Suffix.Length > 0)
            {
                var lastSuffix = postfix.Suffix[^1];

                // Method call: receiver.Method()
                if (lastSuffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    var methodSymbol = model.GetDeclaredSymbol(methodCall);
                    if (methodSymbol is Symbol methodSym && methodSym.OwnershipTier == OwnershipTier.Owned)
                        return true;
                    // Also check if the return type itself is an owned type (for linear/affine return).
                    if (methodSymbol is Symbol returnSym && returnSym.DeclaredType is not null && IsOwnedType(returnSym.DeclaredType))
                        return true;
                }

                // Direct invocation: func()
                if (lastSuffix is InvocationSuffixPostfixSuffixNode invocation)
                {
                    ISymbol? invokedSymbol = null;
                    if (postfix.Target is IdentifierExpressionPrimaryNode targetId)
                        invokedSymbol = model.GetDeclaredSymbol(targetId);
                    invokedSymbol ??= model.GetDeclaredSymbol(invocation);

                    if (invokedSymbol is Symbol funcSym && funcSym.OwnershipTier == OwnershipTier.Owned)
                        return true;
                    // Also check if the return type is an owned type.
                    if (invokedSymbol is Symbol funcRetSym && funcRetSym.DeclaredType is not null && IsOwnedType(funcRetSym.DeclaredType))
                        return true;
                }

                return false;
            }

            // No suffixes — check if the target is a constructor call.
            if (postfix.Target is ObjectCreationExpressionPrimaryNode)
                return true;

            return false;
        }

        // Walk through single-child wrapper nodes (ExpressionNode → AssignmentNode → etc.)
        SyntaxNode? singleChild = null;
        var childCount = 0;
        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                childCount++;
                singleChild = child;
                if (childCount > 1)
                    break;
            }
        }

        if (childCount == 1 && singleChild is not null)
            return IsConstructorOrOwnedCall(singleChild, model);

        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 2c: Owned field modifier validation (Task 7.1)
    // Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 10.7
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates fields declared with the <c>owned</c> modifier:
    /// - The field's type must be an owned type (explicitly declared or inferred).
    /// - If not, reports <c>MIST_OWN_FIELD_NOT_OWNED</c>.
    ///
    /// Note: Assignment restrictions (source must be from owned binding, constructor call,
    /// or owned-returning function) and access restrictions (borrowed/frozen based on
    /// containing instance's binding kind) are flow-sensitive and enforced in Pass 4.
    /// </summary>
    private static void ValidateOwnedFieldModifiers(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            if (symbol.Kind != SymbolKind.Field)
                continue;

            if (symbol is not Symbol fieldSymbol)
                continue;

            // Only check fields with the 'owned' modifier.
            if (fieldSymbol.OwnershipTier != OwnershipTier.Owned)
                continue;

            var fieldType = fieldSymbol.DeclaredType;
            if (fieldType is null)
                continue;

            // Value types are always permitted (they are trivially owned).
            if (IsValueType(fieldType))
                continue;

            // The field's type must be an owned type.
            if (IsOwnedType(fieldType))
                continue;

            // The field is marked 'owned' but its type is not an owned type — report error.
            SyntaxNode fieldNode;
            try { fieldNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            diagnostics.Add(MakeDiagnostic(
                FieldNotOwned,
                $"Field '{symbol.Name}' is declared as 'owned' but its type '{fieldType.DisplayName}' is not an owned type. " +
                $"The 'owned' field modifier requires the field's type to be an owned type.",
                fieldNode,
                model));
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 4: Flow-sensitive analysis (Task 6.2+)
    // Requirements: 10.1, 10.2, 10.3, 10.4, 10.5, 10.6, 7.4, 7.5, 7.9
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Performs flow-sensitive analysis on all method bodies in the semantic model.
    /// Walks statements in control-flow order, tracking ownership state transitions
    /// (moves, borrows) and emitting diagnostics for violations.
    /// </summary>
    private static void AnalyzeFlowSensitive(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            switch (symbol.Kind)
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
    private static void AnalyzeMethodBody(ISymbol methodSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        SyntaxNode declaringNode;
        try { declaringNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        BlockNode? body = declaringNode switch
        {
            MethodDeclarationNode methodDecl => methodDecl.Body.GetBlock(),
            ConstructorDeclarationNode ctorDecl => ctorDecl.Body.GetBlock(),
            _ => null,
        };

        if (body is null)
            return;

        var state = new MethodFlowState();

        // Register owned parameters as Live in the initial state.
        RegisterOwnedParameters(declaringNode, model, state);

        WalkBlock(body, state, model, diagnostics, isLoop: false);
    }

    /// <summary>
    /// Registers all owned parameters of a method/constructor as Live in the flow state.
    /// </summary>
    private static void RegisterOwnedParameters(SyntaxNode declaringNode, ISemanticModel model, MethodFlowState state)
    {
        ParameterListNode? paramList = declaringNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            ConstructorDeclarationNode c => c.Parameters,
            _ => null,
        };

        if (paramList is null)
            return;

        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if (paramSymbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                {
                    state.VariableStates[paramSymbol] = VariableOwnershipState.Live;
                    state.BindingKinds[paramSymbol] = BindingKind.Owned;
                }
            }
        }
    }

    /// <summary>
    /// Walks a block of statements, updating the flow state for each statement.
    /// </summary>
    private static void WalkBlock(
        BlockNode block,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        foreach (var stmt in block.Statements)
        {
            WalkStatement(stmt, state, model, diagnostics, isLoop);
        }
    }

    /// <summary>
    /// Walks a single statement, dispatching to the appropriate handler based on
    /// statement type. This is the main extensibility point — future tasks (6.4, 6.6,
    /// 6.8, 6.10) will add additional checks per statement type here.
    /// </summary>
    private static void WalkStatement(
        StatementNode stmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        switch (stmt)
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
                AnalyzeReturnStatement(returnStmt.ReturnStatement, state, model, diagnostics, isLoop);
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
    private static void AnalyzeVariableDeclaration(
        VariableDeclarationStatementNode varDecl,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        switch (varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
            {
                // Check the initializer for use-after-move before processing the move.
                CheckExpressionForUseAfterMove(typed.Initializer, state, model, diagnostics);

                // Get the declared variable symbol.
                var declaredSymbol = model.GetDeclaredSymbol(typed);
                if (declaredSymbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                {
                    // Register the new owned variable as Live.
                    state.VariableStates[declaredSymbol] = VariableOwnershipState.Live;
                    state.BindingKinds[declaredSymbol] = BindingKind.Owned;

                    // If the initializer is an owned variable, this is a move.
                    var sourceSymbol = TryResolveIdentifierSymbol(typed.Initializer, model);
                    if (sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
                    {
                        PerformMove(sourceSymbol, typed.Initializer, state, model, diagnostics);
                    }
                }
                else if (declaredSymbol is Symbol refSym &&
                         refSym.OwnershipTier is OwnershipTier.Frozen or OwnershipTier.Borrowed or OwnershipTier.ReadonlyRef)
                {
                    // Local frozen, borrowed, or readonly ref variable declaration creates a borrow from the initializer.
                    var sourceSymbol = TryResolveIdentifierSymbol(typed.Initializer, model);
                    if (sourceSymbol is not null &&
                        sourceSymbol is Symbol sourceSym &&
                        sourceSym.OwnershipTier is OwnershipTier.Owned or OwnershipTier.Borrowed)
                    {
                        if (refSym.OwnershipTier == OwnershipTier.ReadonlyRef)
                        {
                            // Readonly ref: lender transitions to LentReadonly.
                            // Check truth table: is LiveView permitted in the current state?
                            if (state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
                            {
                                if (!IsActionPermitted(currentState, AccessKind.LiveView))
                                {
                                    ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.LiveView, typed, state, model, diagnostics);
                                }
                                else
                                {
                                    // Only transition to LentReadonly if currently Live.
                                    // If already LentReadonly or LentFrozen, stay in that state (more restrictive).
                                    if (currentState == VariableOwnershipState.Live)
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
                        else if (refSym.OwnershipTier == OwnershipTier.Frozen)
                        {
                            // Frozen borrow: check truth table before transitioning.
                            if (state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
                            {
                                if (!IsActionPermitted(currentState, AccessKind.Freeze))
                                {
                                    ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.Freeze, typed, state, model, diagnostics);
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
                            if (state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
                            {
                                if (!IsActionPermitted(currentState, AccessKind.Borrow))
                                {
                                    ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.Borrow, typed, state, model, diagnostics);
                                }
                                else
                                {
                                    // Check for existing ref borrow (only one at a time).
                                    if (HasActiveRefBorrow(sourceSymbol, state))
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
                                if (HasActiveRefBorrow(sourceSymbol, state))
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
                    else if (sourceSymbol is not null &&
                             state.BindingKinds.TryGetValue(sourceSymbol, out var sourceBindingKind) &&
                             sourceBindingKind == BindingKind.Unrestricted)
                    {
                        // Unrestricted binding restriction: cannot take borrowed or frozen reference
                        // to an unrestricted binding. Readonly ref to the variable itself is permitted.
                        if (refSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.Frozen)
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
                        if (receiverSymbol is not null &&
                            state.BindingKinds.TryGetValue(receiverSymbol, out var receiverBindingKind) &&
                            receiverBindingKind == BindingKind.Unrestricted)
                        {
                            if (refSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.Frozen)
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
                else if (declaredSymbol is not null)
                {
                    // Unrestricted binding — register binding kind for access rule enforcement.
                    state.BindingKinds[declaredSymbol] = BindingKind.Unrestricted;

                    // If the initializer is an owned variable, this is a move (owned → unrestricted).
                    // The source becomes dead — the destination holds the value without ownership tracking.
                    var sourceSymbol = TryResolveIdentifierSymbol(typed.Initializer, model);
                    if (sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
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

                // Get the declared variable symbol.
                var declaredSymbol = model.GetDeclaredSymbol(inferred);
                if (declaredSymbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                {
                    // Register the new owned variable as Live.
                    state.VariableStates[declaredSymbol] = VariableOwnershipState.Live;
                    state.BindingKinds[declaredSymbol] = BindingKind.Owned;

                    // If the initializer is an owned variable, this is a move.
                    var sourceSymbol = TryResolveIdentifierSymbol(inferred.Initializer, model);
                    if (sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
                    {
                        PerformMove(sourceSymbol, inferred.Initializer, state, model, diagnostics);
                    }
                }
                else if (declaredSymbol is not null)
                {
                    // Unrestricted binding — register binding kind for access rule enforcement.
                    state.BindingKinds[declaredSymbol] = BindingKind.Unrestricted;

                    // If the initializer is an owned variable, this is a move (owned → unrestricted).
                    // The source becomes dead — the destination holds the value without ownership tracking.
                    var sourceSymbol = TryResolveIdentifierSymbol(inferred.Initializer, model);
                    if (sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
                    {
                        PerformMove(sourceSymbol, inferred.Initializer, state, model, diagnostics);
                    }
                }
                break;
            }
        }
    }

    // ── Expression analysis ──────────────────────────────────────────────

    /// <summary>
    /// Analyzes a top-level expression statement for ownership operations.
    /// Handles assignments (which may be moves) and method calls (which may
    /// pass owned arguments).
    /// </summary>
    private static void AnalyzeExpression(
        ExpressionNode expr,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Lambda-form expressions introduce their own body scope; closure capture
        // ownership is handled by the type checker's capture rules, not flow analysis.
        if (expr.AsAssignmentOrNull() is { } assignment)
            AnalyzeAssignment(assignment, state, model, diagnostics, isLoop);
    }

    /// <summary>
    /// Analyzes an assignment node. If this is a reassignment to an owned variable,
    /// it may be a move (from the RHS) and/or a re-assignment (restoring the LHS).
    /// </summary>
    private static void AnalyzeAssignment(
        AssignmentNode assignment,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        if (assignment.Value is not null)
        {
            // This is an assignment: target = value. The RHS is now a full
            // ExpressionNode (widened to reach lambdas). A lambda RHS has no
            // move/ownership effect on the assignment, so resolve the assignment
            // form and skip when it is a lambda.
            var rhsAssignment = assignment.Value.AsAssignmentOrNull();

            // First, check the RHS for use-after-move.
            if (rhsAssignment is not null)
                CheckAssignmentValueForUseAfterMove(rhsAssignment, state, model, diagnostics);

            // Resolve the LHS target symbol.
            var lhsSymbol = TryResolveAssignmentTarget(assignment.Target, model);

            // Resolve the RHS source symbol.
            var rhsSymbol = rhsAssignment is not null
                ? TryResolveAssignmentValueSymbol(rhsAssignment, model)
                : null;

            // Non-storability check: if the RHS is a ref-tier variable and the LHS
            // is a field, emit MIST_OWN_NOT_STORABLE.
            if (rhsSymbol is Symbol rhsSym &&
                rhsSym.OwnershipTier is OwnershipTier.Borrowed or OwnershipTier.ReadonlyRef or OwnershipTier.Frozen)
            {
                var targetFieldSymbol = TryResolveFieldTarget(assignment.Target, model);
                if (targetFieldSymbol is not null && targetFieldSymbol.Kind == SymbolKind.Field)
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
            if (lhsSymbol is not null && IsOwnedVariable(lhsSymbol))
            {
                state.VariableStates[lhsSymbol] = VariableOwnershipState.Live;
                state.MoveLocations.Remove(lhsSymbol);
            }

            // Mutation blocking check: if the assignment target is a field access on a
            // variable in a restricted state, use the truth table to check Write permission.
            var receiverOfFieldAssign = TryResolveFieldAccessReceiver(assignment.Target, model);
            if (receiverOfFieldAssign is not null &&
                state.VariableStates.TryGetValue(receiverOfFieldAssign, out var recvState))
            {
                if (!IsActionPermitted(recvState, AccessKind.Write))
                {
                    ReportTruthTableViolation(receiverOfFieldAssign, recvState, AccessKind.Write, assignment, state, model, diagnostics);
                }
            }

            // If the RHS is an owned variable being assigned to an owned or unrestricted LHS, it's a move.
            // Owned → owned: ownership transfers. Owned → unrestricted: source becomes dead.
            if (rhsSymbol is not null && IsOwnedVariable(rhsSymbol) && lhsSymbol is not null)
            {
                if (IsOwnedVariable(lhsSymbol))
                {
                    PerformMove(rhsSymbol, assignment, state, model, diagnostics);
                }
                else if (state.BindingKinds.TryGetValue(lhsSymbol, out var lhsKind) &&
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
    private static void AnalyzeIfStatement(
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
        if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            // Fork from the pre-if state for the else branch.
            var elseState = thenState.Fork();

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
        else if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
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
    private static void AnalyzeWhileStatement(
        WhileStatementNode whileStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check the condition for use-after-move.
        CheckExpressionForUseAfterMove(whileStmt.Condition, state, model, diagnostics);

        // Snapshot which owned variables are Live before the loop body.
        var liveBeforeLoop = new Dictionary<ISymbol, VariableOwnershipState>();
        foreach (var (symbol, varState) in state.VariableStates)
        {
            if (varState == VariableOwnershipState.Live && IsOwnedVariable(symbol))
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
    private static void AnalyzeForStatement(
        ForStatementNode forStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check initializer for use-after-move.
        if (forStmt.Initializer is not null)
        {
            foreach (var child in forStmt.Initializer.Children())
            {
                if (child is ExpressionNode expr)
                    CheckExpressionForUseAfterMove(expr, state, model, diagnostics);
            }
        }

        // Check condition for use-after-move.
        if (forStmt.Condition is not null)
            CheckExpressionForUseAfterMove(forStmt.Condition, state, model, diagnostics);

        // Snapshot which owned variables are Live before the loop body.
        var liveBeforeLoop = new Dictionary<ISymbol, VariableOwnershipState>();
        foreach (var (symbol, varState) in state.VariableStates)
        {
            if (varState == VariableOwnershipState.Live && IsOwnedVariable(symbol))
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
        if (forStmt.Iterator is not null)
            CheckExpressionForUseAfterMove(forStmt.Iterator, state, model, diagnostics);
    }

    /// <summary>
    /// Analyzes a foreach loop. Moves inside the loop body without re-assignment
    /// emit <c>MIST_OWN_LOOP_MOVE</c>.
    /// </summary>
    private static void AnalyzeForeachStatement(
        ForeachStatementNode foreachStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check collection expression for use-after-move.
        CheckExpressionForUseAfterMove(foreachStmt.Collection, state, model, diagnostics);

        // Snapshot which owned variables are Live before the loop body.
        var liveBeforeLoop = new Dictionary<ISymbol, VariableOwnershipState>();
        foreach (var (symbol, varState) in state.VariableStates)
        {
            if (varState == VariableOwnershipState.Live && IsOwnedVariable(symbol))
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
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        if (returnStmt.Value is not null)
        {
            CheckExpressionForUseAfterMove(returnStmt.Value, state, model, diagnostics);

            // If returning an owned variable, it's a move out of the method.
            var sourceSymbol = TryResolveIdentifierSymbol(returnStmt.Value, model);
            if (sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
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
        if (state.VariableStates.TryGetValue(sourceSymbol, out var currentState))
        {
            if (!IsActionPermitted(currentState, AccessKind.Move))
            {
                ReportTruthTableViolation(sourceSymbol, currentState, AccessKind.Move, moveNode, state, model, diagnostics);
                return;
            }
        }

        state.VariableStates[sourceSymbol] = VariableOwnershipState.Moved;
        state.MoveLocations[sourceSymbol] = new Common.Text.SourceSpan(moveNode.Span.Start, moveNode.Span.Length);
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
        foreach (var (symbol, _) in liveBeforeLoop)
        {
            if (state.VariableStates.TryGetValue(symbol, out var afterState) &&
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
        if (expr.AsAssignmentOrNull() is { } assignment)
            CheckAssignmentValueForUseAfterMove(assignment, state, model, diagnostics);
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
        if (assignment.Value is not null && assignment.Value.AsAssignmentOrNull() is { } rhsAssignment)
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
        if (node is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if (symbol is not null && IsOwnedVariable(symbol))
            {
                if (state.VariableStates.TryGetValue(symbol, out var varState) &&
                    !IsActionPermitted(varState, AccessKind.Read))
                {
                    ReportTruthTableViolation(symbol, varState, AccessKind.Read, identifier, state, model, diagnostics);
                }
            }
        }

        // Recursively check children.
        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                CheckLogicalOrForUseAfterMove(child, state, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks method calls in an expression for owned argument passing (moves).
    /// When an owned variable is passed to an owned parameter, it is moved.
    /// </summary>
    private static void CheckMethodCallsForMoves(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Look for PostfixNode with method call suffixes.
        if (node is PostfixNode postfix)
        {
            // Check mutation blocking: if the receiver is in a restricted state,
            // use the truth table to check whether the access is permitted.
            if (postfix.Suffix.Length > 0 && postfix.Suffix[0] is MethodCallSuffixPostfixSuffixNode firstMethodCall)
            {
                var receiverSymbol = model.GetDeclaredSymbol(postfix.Target);
                if (receiverSymbol is not null &&
                    state.VariableStates.TryGetValue(receiverSymbol, out var receiverState))
                {
                    if (receiverState == VariableOwnershipState.LentRef)
                    {
                        // LentRef: all access is blocked (Read is false in truth table).
                        if (!IsActionPermitted(receiverState, AccessKind.Read))
                        {
                            ReportTruthTableViolation(receiverSymbol, receiverState, AccessKind.Read, firstMethodCall, state, model, diagnostics);
                        }
                    }
                    else if (receiverState == VariableOwnershipState.LentFrozen)
                    {
                        // LentFrozen: check if the method is non-readonly (mutation attempt = Write).
                        var receiverType = receiverSymbol.DeclaredType;
                        if (receiverType is not null)
                        {
                            var methodSym = LookupMemberByName(receiverType, firstMethodCall.Name.Lexeme);
                            if (methodSym is not null && !IsReadonlyMember(methodSym))
                            {
                                if (!IsActionPermitted(receiverState, AccessKind.Write))
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
                    else if (receiverState == VariableOwnershipState.Moved)
                    {
                        // Moved: all access is blocked.
                        if (!IsActionPermitted(receiverState, AccessKind.Read))
                        {
                            ReportTruthTableViolation(receiverSymbol, receiverState, AccessKind.Read, firstMethodCall, state, model, diagnostics);
                        }
                    }
                }
            }

            foreach (var suffix in postfix.Suffix)
            {
                if (suffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    CheckMethodCallArgumentsForMoves(methodCall, state, model, diagnostics, isLoop);
                }
                else if (suffix is InvocationSuffixPostfixSuffixNode invocation)
                {
                    CheckInvocationArgumentsForMoves(invocation, postfix.Target, state, model, diagnostics, isLoop);
                }
            }
        }

        // Recursively check children.
        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                CheckMethodCallsForMoves(child, state, model, diagnostics, isLoop);
            }
        }
    }

    /// <summary>
    /// Checks arguments of a method call for owned parameter passing (moves) and
    /// lending compatibility violations.
    /// </summary>
    private static void CheckMethodCallArgumentsForMoves(
        MethodCallSuffixPostfixSuffixNode methodCall,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Resolve the method symbol to get parameter ownership tiers.
        var methodSymbol = model.GetDeclaredSymbol(methodCall);
        if (methodSymbol is null)
            return;

        // Get parameter symbols.
        SyntaxNode methodDeclNode;
        try { methodDeclNode = model.GetDeclaringNode(methodSymbol); }
        catch { return; }

        var paramSymbols = GetParameterSymbols(methodDeclNode, model);
        if (paramSymbols is null)
            return;

        // Get argument expressions.
        var argExprs = CollectArgumentExpressions(methodCall.Arguments);

        var count = Math.Min(paramSymbols.Count, argExprs.Count);
        for (var i = 0; i < count; i++)
        {
            if (paramSymbols[i] is Symbol paramSym)
            {
                // Skip ownership checks for ref parameters — they use a different
                // mechanism (variable-level indirection) that is orthogonal to ownership lending.
                if (paramSym.RefKind != RefKind.None)
                    continue;

                if (paramSym.OwnershipTier == OwnershipTier.Owned)
                {
                    // This parameter is owned — passing an owned variable here is a move.
                    var argSymbol = TryResolveIdentifierSymbol(argExprs[i], model);
                    if (argSymbol is not null && IsOwnedVariable(argSymbol))
                    {
                        PerformMove(argSymbol, argExprs[i], state, model, diagnostics);
                    }
                }

                // Check lending compatibility for all tier combinations.
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
    private static void CheckInvocationArgumentsForMoves(
        InvocationSuffixPostfixSuffixNode invocation,
        PrimaryNode target,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Resolve the invoked symbol from the target identifier first,
        // then fall back to the invocation node itself.
        ISymbol? invokedSymbol = null;
        if (target is IdentifierExpressionPrimaryNode targetId)
        {
            invokedSymbol = model.GetDeclaredSymbol(targetId);
        }
        invokedSymbol ??= model.GetDeclaredSymbol(invocation);
        if (invokedSymbol is null)
            return;

        // Get parameter symbols.
        SyntaxNode declNode;
        try { declNode = model.GetDeclaringNode(invokedSymbol); }
        catch { return; }

        var paramSymbols = GetParameterSymbols(declNode, model);
        if (paramSymbols is null)
            return;

        // Get argument expressions.
        var argExprs = CollectArgumentExpressions(invocation.Arguments);

        var count = Math.Min(paramSymbols.Count, argExprs.Count);
        for (var i = 0; i < count; i++)
        {
            if (paramSymbols[i] is Symbol paramSym)
            {
                // Skip ownership checks for ref parameters — they use a different
                // mechanism (variable-level indirection) that is orthogonal to ownership lending.
                if (paramSym.RefKind != RefKind.None)
                    continue;

                if (paramSym.OwnershipTier == OwnershipTier.Owned)
                {
                    var argSymbol = TryResolveIdentifierSymbol(argExprs[i], model);
                    if (argSymbol is not null && IsOwnedVariable(argSymbol))
                    {
                        PerformMove(argSymbol, argExprs[i], state, model, diagnostics);
                    }
                }

                // Check lending compatibility for all tier combinations.
                CheckLendingCompatibility(argExprs[i], paramSym, state, model, diagnostics);
            }
        }

        // Check ref exclusivity: same variable passed to multiple ref parameters.
        CheckRefExclusivity(argExprs, paramSymbols, model, diagnostics);
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
        if (argSymbol is not Symbol argSym)
            return;

        // Value types always satisfy any tier requirement.
        if (argSym.DeclaredType is not null && IsValueType(argSym.DeclaredType))
            return;

        var sourceTier = argSym.OwnershipTier;

        // Use BindingKind from MethodFlowState to determine if the argument is an
        // unrestricted binding. This provides flow-sensitive detection that covers
        // cases where the symbol's declared tier alone may not tell the full story.
        if (targetTier is OwnershipTier.Borrowed or OwnershipTier.Frozen &&
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
        if (PermittedLendingPairs.Contains((sourceTier, targetTier)))
            return;

        // Emit the appropriate diagnostic.
        if (sourceTier == OwnershipTier.Unrestricted &&
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
        if (node is ExpressionNode expr)
            return expr.AsAssignmentOrNull() is { } exprAssignment
                ? TryResolveIdentifierSymbol(exprAssignment, model)
                : null; // Lambda form is not a simple identifier reference.

        if (node is AssignmentNode assignment)
        {
            if (assignment.Value is not null)
                return null; // Chained assignment, not a simple identifier.
            return TryResolveIdentifierSymbol(assignment.Target, model);
        }

        // Walk down the single-child chain to find a PostfixNode with no suffixes.
        if (node is PostfixNode postfix)
        {
            if (postfix.Suffix.Length > 0)
                return null; // Has suffixes (member access, method call, etc.)
            return TryResolveIdentifierSymbol(postfix.Target, model);
        }

        if (node is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if (symbol is not null &&
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
        foreach (var child in node.Children())
        {
            if (child is not null)
                nonNullChildren.Add(child);
        }

        if (nonNullChildren.Count == 1)
            return TryResolveIdentifierSymbol(nonNullChildren[0], model);

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
        if (target is ExpressionNode expr)
            return expr.AsAssignmentOrNull() is { } exprAssignment
                ? TryResolveFieldTarget(exprAssignment, model)
                : null; // Lambda form is not a field target.

        if (target is AssignmentNode assignment)
        {
            if (assignment.Value is not null)
                return null;
            return TryResolveFieldTarget(assignment.Target, model);
        }

        if (target is PostfixNode postfix)
        {
            if (postfix.Suffix.Length > 0)
                return null;
            return TryResolveFieldTarget(postfix.Target, model);
        }

        if (target is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if (symbol is not null && symbol.Kind == SymbolKind.Field)
                return symbol;
            return null;
        }

        // Walk single-child chains.
        var nonNullChildren = new List<SyntaxNode>();
        foreach (var child in target.Children())
        {
            if (child is not null)
                nonNullChildren.Add(child);
        }

        if (nonNullChildren.Count == 1)
            return TryResolveFieldTarget(nonNullChildren[0], model);

        return null;
    }

    /// <summary>
    /// Attempts to resolve the value side of an assignment to a symbol.
    /// </summary>
    private static ISymbol? TryResolveAssignmentValueSymbol(AssignmentNode value, ISemanticModel model)
    {
        if (value.Value is not null)
            return null; // Chained assignment.
        return TryResolveIdentifierSymbol(value.Target, model);
    }

    /// <summary>
    /// Collects argument expressions from an ArgumentListNode.
    /// </summary>
    private static List<SyntaxNode> CollectArgumentExpressions(ArgumentListNode? argList)
    {
        var result = new List<SyntaxNode>();
        if (argList is null)
            return result;

        // The ArgumentListNode has Argument (first) and Arguments (rest),
        // each wrapping an ExpressionNode.
        result.Add(ArgumentListNodeExtensions.ExtractExpression(argList.Argument));
        foreach (var restArg in argList.Arguments)
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
        if (symbol is not Symbol sym)
            return false;
        if (sym.Kind is not (SymbolKind.LocalVariable or SymbolKind.Parameter))
            return false;
        return sym.OwnershipTier == OwnershipTier.Owned;
    }

    /// <summary>
    /// Looks up a member symbol by name in the given type's member scope.
    /// Returns null if not found.
    /// </summary>
    private static ISymbol? LookupMemberByName(IType type, string name)
    {
        IScope? memberScope = type switch
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
        if (symbol is not Symbol sym)
            return false;
        return sym.ReadonlyStatus is ReadonlyStatus.ExplicitReadonly or ReadonlyStatus.InferredReadonly;
    }

    /// <summary>
    /// Checks whether there is already an active ref borrow from the given lender.
    /// </summary>
    private static bool HasActiveRefBorrow(ISymbol lender, MethodFlowState state)
    {
        foreach (var borrow in state.ActiveBorrows)
        {
            if (ReferenceEquals(borrow.Lender, lender) && borrow.BorrowTier == OwnershipTier.Borrowed)
                return true;
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
        while (current is not PostfixNode)
        {
            nonNullChildren.Clear();
            foreach (var child in current.Children())
            {
                if (child is not null)
                    nonNullChildren.Add(child);
            }
            if (nonNullChildren.Count != 1)
                return null;
            current = nonNullChildren[0];
        }

        if (current is PostfixNode postfix && postfix.Suffix.Length > 0 &&
            postfix.Suffix[^1] is MemberAccessSuffixPostfixSuffixNode)
        {
            // The receiver is the PostfixNode's target (the identifier before the dot).
            if (postfix.Target is IdentifierExpressionPrimaryNode receiverId)
            {
                var symbol = model.GetDeclaredSymbol(receiverId);
                if (symbol is not null &&
                    symbol.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter)
                {
                    return symbol;
                }
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
    /// Returns <c>true</c> if the given type structurally satisfies the owned-type field
    /// restriction — all reference-type fields are themselves owned types. This allows
    /// classes that are not explicitly declared as <c>owned</c> but have only value-type
    /// or owned-type fields to be used in owned bindings.
    /// </summary>
    private static bool SatisfiesOwnedTypeFieldRestriction(IType type)
    {
        IScope? memberScope = type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };

        if (memberScope is null)
            return false;

        foreach (var member in memberScope.Members)
        {
            if (member.Kind != SymbolKind.Field)
                continue;

            var fieldType = member.DeclaredType;
            if (fieldType is null)
                continue;

            // Value types are always fine.
            if (IsValueType(fieldType))
                continue;

            // Reference-type field must be an owned type.
            if (!IsOwnedType(fieldType))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Checks whether a modifier list contains the <c>owned</c> modifier.
    /// </summary>
    private static bool HasOwnedModifier(System.Collections.Immutable.ImmutableArray<ModifierNode> modifiers)
    {
        foreach (var modifier in modifiers)
        {
            if (modifier is OwnedModifierModifierNode)
                return true;
        }
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Pass 3: Ownership signature matching validation (Task 4.4)
    // Requirements: 9.1, 9.2, 9.3, 9.4, 2.5
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Validates ownership signature matching for interface implementations and
    /// structural trait satisfaction. Also rejects non-owned specifiers on return types.
    /// </summary>
    private static void ValidateSignatureMatching(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // 1. Check invalid return type specifiers on all method declarations.
        ValidateReturnTypeSpecifiers(model, diagnostics);

        // 2. Check interface method implementation ownership matching.
        ValidateInterfaceOwnershipMatching(model, diagnostics);

        // 3. Check structural trait matching ownership consistency.
        ValidateTraitOwnershipMatching(model, diagnostics);
    }

    /// <summary>
    /// Rejects non-owned ownership specifiers on method return types.
    /// Only <c>owned</c> is valid as a return type specifier; <c>ref</c>,
    /// <c>readonly ref</c>, and <c>frozen ref</c> are invalid (Requirement 2.5).
    /// </summary>
    private static void ValidateReturnTypeSpecifiers(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind != SymbolKind.Method)
                continue;

            if (symbol is not Symbol methodSymbol)
                continue;

            // Only non-owned specifiers on return types are invalid.
            if (methodSymbol.OwnershipTier is OwnershipTier.Borrowed
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
    /// Checks that interface method implementations match ownership specifiers
    /// on parameters and return types exactly (Requirements 9.1, 9.2).
    /// </summary>
    private static void ValidateInterfaceOwnershipMatching(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin == SymbolOrigin.Backend)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var type = symbol.DeclaredType;
            IReadOnlyList<IType>? interfaces = type switch
            {
                ClassType ct => ct.Interfaces,
                StructType st => st.Interfaces,
                _ => null,
            };

            if (interfaces is null || interfaces.Count == 0)
                continue;

            IScope? memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is null)
                continue;

            var visited = new HashSet<InterfaceType>(ReferenceEqualityComparer.Instance);
            CheckInterfaceOwnershipContracts(symbol, interfaces, memberScope, model, diagnostics, visited);
        }
    }

    /// <summary>
    /// Recursively checks ownership specifier matching for all interface methods.
    /// </summary>
    private static void CheckInterfaceOwnershipContracts(
        ISymbol typeSymbol,
        IReadOnlyList<IType> interfaces,
        IScope memberScope,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        HashSet<InterfaceType> visited)
    {
        foreach (var ifaceType in interfaces)
        {
            if (ifaceType is not InterfaceType iface)
                continue;

            if (!visited.Add(iface))
                continue;

            foreach (var ifaceMember in iface.MemberScope.Members)
            {
                if (ifaceMember.Kind != SymbolKind.Method)
                    continue;

                // Find the implementing method in the type's member scope.
                var implMember = FindMethodInScope(ifaceMember.Name, memberScope);
                if (implMember is null)
                    continue; // Missing implementation is reported by the type checker.

                CheckMethodOwnershipMatch(
                    typeSymbol, ifaceMember, implMember, iface.DisplayName, model, diagnostics);
            }

            // Recurse into parent interfaces.
            if (iface.Interfaces.Count > 0)
            {
                CheckInterfaceOwnershipContracts(typeSymbol, iface.Interfaces, memberScope, model, diagnostics, visited);
            }
        }
    }

    /// <summary>
    /// Checks ownership specifier matching for structural trait satisfaction
    /// (Requirement 9.3).
    /// </summary>
    private static void ValidateTraitOwnershipMatching(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var (satisfiedType, trait, satisfaction) in model.TraitSatisfactionMap.All())
        {
            var traitType = trait switch
            {
                TraitType tt => tt,
                ConstructedTraitType ctt => ctt.GenericTraitDefinition,
                _ => null,
            };

            if (traitType is null)
                continue;

            // Find the type symbol for the implementing type.
            ISymbol? typeSymbol = null;
            foreach (var sym in model.AllSymbols)
            {
                if (sym.Origin == SymbolOrigin.Backend)
                    continue;
                if (sym.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                    continue;
                if (ReferenceEquals(sym.DeclaredType, satisfiedType))
                {
                    typeSymbol = sym;
                    break;
                }
            }

            if (typeSymbol is null)
                continue;

            var traitDisplayName = trait.DisplayName;

            foreach (var traitMember in traitType.MemberScope.Members)
            {
                if (traitMember.Kind != SymbolKind.Method)
                    continue;

                // Skip type parameter symbols.
                if (traitMember.DeclaredType is TypeParameterType)
                    continue;

                // Find the implementing member via the satisfaction map.
                ISymbol? implMember = null;
                if (satisfaction.StructuralMatches.TryGetValue(traitMember.Name, out var structMatch))
                    implMember = structMatch;
                else if (satisfaction.WithMappings.TryGetValue(traitMember.Name, out var withMatch))
                    implMember = withMatch;

                if (implMember is null || implMember.Kind != SymbolKind.Method)
                    continue;

                CheckMethodOwnershipMatch(
                    typeSymbol, traitMember, implMember, traitDisplayName, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Compares ownership specifiers on parameters and return type between an
    /// interface/trait method and its implementation. Emits MIST_OWN_SIGNATURE_MISMATCH
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
        if (implMethod is Symbol implSym && contractMethod is Symbol contractSym)
        {
            if (implSym.OwnershipTier != contractSym.OwnershipTier)
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

        if (contractParams is null || implParams is null)
            return;

        var count = Math.Min(contractParams.Count, implParams.Count);
        for (var i = 0; i < count; i++)
        {
            var contractParam = contractParams[i] as Symbol;
            var implParam = implParams[i] as Symbol;

            if (contractParam is null || implParam is null)
                continue;

            if (contractParam.OwnershipTier != implParam.OwnershipTier)
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
        ParameterListNode? paramList = methodNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            InterfaceMethodDeclarationNode im => im.Parameters,
            TraitMethodDeclarationNode tm => tm.Parameters,
            _ => null,
        };

        if (paramList is null)
            return null;

        var result = new List<ISymbol>();
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode paramNode)
            {
                var paramSymbol = model.GetDeclaredSymbol(paramNode);
                if (paramSymbol is not null)
                    result.Add(paramSymbol);
            }
        }

        return result;
    }

    /// <summary>
    /// Finds a method by name in a scope.
    /// </summary>
    private static ISymbol? FindMethodInScope(string name, IScope scope)
    {
        foreach (var member in scope.Members)
        {
            if (member.Name == name && member.Kind == SymbolKind.Method)
                return member;
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
        Common.Text.LinePositionSpan? location = null;
        string? compilationUnitPath = null;

        var span = node.Span;
        if (span.Length > 0)
        {
            var asts = model.Asts;
            var units = model.Units;
            var astIndex = -1;

            for (var i = 0; i < asts.Length; i++)
            {
                var astSpan = asts[i].Span;
                if (span.Start >= astSpan.Start && span.Start + span.Length <= astSpan.Start + astSpan.Length)
                {
                    astIndex = i;
                    break;
                }
            }

            if (astIndex >= 0 && astIndex < units.Count)
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

    // ── Non-storability enforcement (Task 6.6) ───────────────────────────

    /// <summary>
    /// Validates that ref, readonly ref, and frozen ref variables are not stored
    /// in fields, collections, or returned from methods.
    /// </summary>
    private static void ValidateNonStorability(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            // Only check field declarations.
            if (symbol.Kind != SymbolKind.Field)
                continue;

            if (symbol is not Symbol fieldSymbol)
                continue;

            // If the field has a non-storable ownership tier, emit MIST_OWN_NOT_STORABLE.
            if (fieldSymbol.OwnershipTier is OwnershipTier.Borrowed
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
        for (var i = 0; i < count; i++)
        {
            if (paramSymbols[i] is Symbol paramSym && paramSym.OwnershipTier == OwnershipTier.Borrowed)
            {
                var argSymbol = TryResolveIdentifierSymbol(argExprs[i], model);
                if (argSymbol is not null)
                {
                    if (!refLentSymbols.Add(argSymbol))
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
}
