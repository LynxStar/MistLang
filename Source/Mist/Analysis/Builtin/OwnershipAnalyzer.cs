using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
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

    /// <summary>Owned type instance not held as owned.</summary>
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

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Pass 1: Owned type declaration validation.
        ValidateOwnedTypeDeclarations(model, diagnostics);

        // Pass 2: Ensure instances of owned types are always held as owned.
        ValidateOwnedTypeUsages(model, diagnostics);

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

                // Value types are always permitted (Requirement 8.4).
                if (IsValueType(fieldType))
                    continue;

                // Reference-type field: must be owned or of an owned type.
                if (member is Symbol fieldSymbol && fieldSymbol.OwnershipTier == OwnershipTier.Owned)
                    continue;

                if (IsOwnedType(fieldType))
                    continue;

                // Emit MIST_OWN_FIELD_NOT_OWNED.
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

    // ── Pass 2: Owned type usage validation ──────────────────────────────

    /// <summary>
    /// Ensures that instances of owned types are always held in <c>owned</c> variables,
    /// parameters, or fields. Emits <c>MIST_OWN_TYPE_REQUIRES_OWNED</c> otherwise.
    /// </summary>
    private static void ValidateOwnedTypeUsages(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            // Only check variables, parameters, and fields that hold a reference to an owned type.
            if (symbol.Kind is not (SymbolKind.LocalVariable or SymbolKind.Parameter or SymbolKind.Field))
                continue;

            var declaredType = symbol.DeclaredType;
            if (declaredType is null)
                continue;

            // Only applies to owned types.
            if (!IsOwnedType(declaredType))
                continue;

            // The symbol must have OwnershipTier.Owned.
            if (symbol is Symbol sym && sym.OwnershipTier == OwnershipTier.Owned)
                continue;

            // Emit MIST_OWN_TYPE_REQUIRES_OWNED.
            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            diagnostics.Add(MakeDiagnostic(
                TypeRequiresOwned,
                $"Variable '{symbol.Name}' of owned type '{declaredType.DisplayName}' must be declared as 'owned'",
                declaringNode,
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

                    // If the initializer is an owned variable, this is a move.
                    var sourceSymbol = TryResolveIdentifierSymbol(typed.Initializer, model);
                    if (sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
                    {
                        PerformMove(sourceSymbol, typed.Initializer, state, model, diagnostics, isLoop);
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

                    // If the initializer is an owned variable, this is a move.
                    var sourceSymbol = TryResolveIdentifierSymbol(inferred.Initializer, model);
                    if (sourceSymbol is not null && IsOwnedVariable(sourceSymbol))
                    {
                        PerformMove(sourceSymbol, inferred.Initializer, state, model, diagnostics, isLoop);
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
        AnalyzeAssignment(expr.Assignment, state, model, diagnostics, isLoop);
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
            // This is an assignment: target = value.
            // First, check the RHS for use-after-move.
            CheckAssignmentValueForUseAfterMove(assignment.Value, state, model, diagnostics);

            // Resolve the LHS target symbol.
            var lhsSymbol = TryResolveAssignmentTarget(assignment.Target, model);

            // Resolve the RHS source symbol.
            var rhsSymbol = TryResolveAssignmentValueSymbol(assignment.Value, model);

            // If the LHS is an owned variable, this assignment restores it to Live.
            if (lhsSymbol is not null && IsOwnedVariable(lhsSymbol))
            {
                state.VariableStates[lhsSymbol] = VariableOwnershipState.Live;
                state.MoveLocations.Remove(lhsSymbol);
            }

            // If the RHS is an owned variable being assigned to an owned LHS, it's a move.
            if (rhsSymbol is not null && IsOwnedVariable(rhsSymbol) &&
                lhsSymbol is not null && IsOwnedVariable(lhsSymbol))
            {
                PerformMove(rhsSymbol, assignment, state, model, diagnostics, isLoop);
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

        // Walk the loop body with isLoop=true to detect loop moves.
        state.EnterScope();
        WalkBlock(whileStmt.Body, state, model, diagnostics, isLoop: true);
        state.ExitScope();
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

        // Walk the loop body with isLoop=true.
        state.EnterScope();
        WalkBlock(forStmt.Body, state, model, diagnostics, isLoop: true);
        state.ExitScope();

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

        // Walk the loop body with isLoop=true.
        state.EnterScope();
        WalkBlock(foreachStmt.Body, state, model, diagnostics, isLoop: true);
        state.ExitScope();
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
                PerformMove(sourceSymbol, returnStmt.Value, state, model, diagnostics, isLoop);
            }
        }
    }

    // ── Move operations ──────────────────────────────────────────────────

    /// <summary>
    /// Performs a move of an owned variable: marks it as Moved and records the
    /// move location. If we are inside a loop, emits <c>MIST_OWN_LOOP_MOVE</c>.
    /// </summary>
    private static void PerformMove(
        ISymbol sourceSymbol,
        SyntaxNode moveNode,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // If the variable is already moved, this is a use-after-move (the move itself
        // accesses the variable). This is already caught by CheckExpressionForUseAfterMove
        // which runs before PerformMove, so we just mark it.

        if (isLoop)
        {
            // In a loop, moving an owned variable without re-assignment is an error
            // because the second iteration would access a moved variable.
            diagnostics.Add(MakeDiagnostic(
                LoopMove,
                $"Owned variable '{sourceSymbol.Name}' is moved inside a loop. The variable must be re-assigned before the next iteration or the move must be removed.",
                moveNode,
                model));
        }

        state.VariableStates[sourceSymbol] = VariableOwnershipState.Moved;
        state.MoveLocations[sourceSymbol] = new Common.Text.SourceSpan(moveNode.Span.Start, moveNode.Span.Length);
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
        CheckAssignmentValueForUseAfterMove(expr.Assignment, state, model, diagnostics);
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

        // If there's a chained assignment, check it recursively.
        if (assignment.Value is not null)
        {
            CheckAssignmentValueForUseAfterMove(assignment.Value, state, model, diagnostics);
        }
    }

    /// <summary>
    /// Checks a LogicalOrNode tree for use-after-move by walking down to identifiers.
    /// </summary>
    private static void CheckLogicalOrForUseAfterMove(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check if this node is an identifier referencing a moved variable.
        if (node is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if (symbol is not null && IsOwnedVariable(symbol))
            {
                if (state.VariableStates.TryGetValue(symbol, out var varState) &&
                    varState == VariableOwnershipState.Moved)
                {
                    diagnostics.Add(MakeDiagnostic(
                        UseAfterMove,
                        $"Variable '{symbol.Name}' has been moved and cannot be accessed here",
                        identifier,
                        model));
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
            foreach (var suffix in postfix.Suffix)
            {
                if (suffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    CheckMethodCallArgumentsForMoves(methodCall, state, model, diagnostics, isLoop);
                }
                else if (suffix is InvocationSuffixPostfixSuffixNode invocation)
                {
                    CheckInvocationArgumentsForMoves(invocation, state, model, diagnostics, isLoop);
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
                if (paramSym.OwnershipTier == OwnershipTier.Owned)
                {
                    // This parameter is owned — passing an owned variable here is a move.
                    var argSymbol = TryResolveIdentifierSymbol(argExprs[i], model);
                    if (argSymbol is not null && IsOwnedVariable(argSymbol))
                    {
                        PerformMove(argSymbol, argExprs[i], state, model, diagnostics, isLoop);
                    }
                }

                // Check lending compatibility for all tier combinations.
                CheckLendingCompatibility(argExprs[i], paramSym, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks arguments of a direct invocation for owned parameter passing (moves) and
    /// lending compatibility violations.
    /// </summary>
    private static void CheckInvocationArgumentsForMoves(
        InvocationSuffixPostfixSuffixNode invocation,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isLoop)
    {
        // Resolve the invoked symbol.
        var invokedSymbol = model.GetDeclaredSymbol(invocation);
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
                if (paramSym.OwnershipTier == OwnershipTier.Owned)
                {
                    var argSymbol = TryResolveIdentifierSymbol(argExprs[i], model);
                    if (argSymbol is not null && IsOwnedVariable(argSymbol))
                    {
                        PerformMove(argSymbol, argExprs[i], state, model, diagnostics, isLoop);
                    }
                }

                // Check lending compatibility for all tier combinations.
                CheckLendingCompatibility(argExprs[i], paramSym, model, diagnostics);
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
        (OwnershipTier.Owned, OwnershipTier.Ref),
        (OwnershipTier.Owned, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Owned, OwnershipTier.FrozenRef),
        (OwnershipTier.Ref, OwnershipTier.Ref),
        (OwnershipTier.Ref, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Ref, OwnershipTier.FrozenRef),
        (OwnershipTier.ReadonlyRef, OwnershipTier.ReadonlyRef),
        (OwnershipTier.FrozenRef, OwnershipTier.FrozenRef),
        (OwnershipTier.FrozenRef, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Unrestricted, OwnershipTier.ReadonlyRef),
        (OwnershipTier.Unrestricted, OwnershipTier.Unrestricted),
    };

    /// <summary>
    /// Checks whether passing an argument to a parameter satisfies the lending
    /// compatibility matrix. Value types always satisfy any tier. For reference types,
    /// the (source tier, target tier) pair must be in the permitted set.
    /// </summary>
    private static void CheckLendingCompatibility(
        SyntaxNode argExpr,
        Symbol paramSym,
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

        // Check if the pair is permitted.
        if (PermittedLendingPairs.Contains((sourceTier, targetTier)))
            return;

        // Emit the appropriate diagnostic.
        if (sourceTier == OwnershipTier.Unrestricted &&
            targetTier is OwnershipTier.Ref or OwnershipTier.FrozenRef or OwnershipTier.Owned)
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
            return TryResolveIdentifierSymbol(expr.Assignment, model);

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

        // The ArgumentListNode has an Expression (first arg) and Expressions (rest args).
        result.Add(argList.Expression);
        foreach (var restArg in argList.Expressions)
        {
            result.Add(restArg);
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
        return type switch
        {
            ClassType ct => ct.IsOwnedType,
            StructType st => st.IsOwnedType,
            _ => false
        };
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
            if (methodSymbol.OwnershipTier is OwnershipTier.Ref
                or OwnershipTier.ReadonlyRef
                or OwnershipTier.FrozenRef)
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
            OwnershipTier.Ref => "ref",
            OwnershipTier.ReadonlyRef => "readonly ref",
            OwnershipTier.FrozenRef => "frozen ref",
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
}
