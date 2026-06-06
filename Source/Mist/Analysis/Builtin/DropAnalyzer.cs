using System.Collections.Immutable;
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
/// Built-in analyzer that enforces drop-semantics rules against the semantic model.
/// This analyzer follows the <see cref="OwnershipAnalyzer"/> pattern: it defines
/// analyzer-local descriptive diagnostic IDs (not central numeric codes) and builds
/// diagnostics through a local <c>MakeDiagnostic</c> helper.
/// <para>
/// It is registered AFTER <see cref="OwnershipAnalyzer"/> so that drop analysis observes
/// the ownership state (<c>IsOwnedType</c>/<c>Discipline</c>) the ownership pipeline
/// establishes.
/// </para>
/// <para>
/// This file currently implements the declaration-level validations (conflicting
/// modifiers, <c>Drop()</c> parameter rejection, and non-owned container with an
/// affine/linear field). Flow-sensitive drop-state tracking (use-after-drop/move and
/// field-drop-outside-container) is implemented in a later task; see the marked section
/// below.
/// </para>
/// </summary>
public sealed class DropAnalyzer : IAnalyzer
{
    public string AnalyzerId => "DROP";

    // ── Diagnostic IDs ───────────────────────────────────────────────────

    /// <summary>Use-after-drop/move on a linear value.</summary>
    internal static readonly DiagnosticId UseAfterMove = new("MIST_DROP_USE_AFTER_MOVE");

    /// <summary>Use-after-drop on an affine value.</summary>
    internal static readonly DiagnosticId UseAfterDrop = new("MIST_DROP_USE_AFTER_DROP");

    /// <summary>Non-owned container declares an affine or linear field.</summary>
    internal static readonly DiagnosticId FieldNotOwned = new("MIST_DROP_FIELD_NOT_OWNED");

    /// <summary>Field <c>Drop()</c> called outside of the containing type's <c>Drop()</c> method.</summary>
    internal static readonly DiagnosticId FieldOutsideContainer = new("MIST_DROP_FIELD_OUTSIDE_CONTAINER");

    /// <summary>Conflicting substructural/ownership type modifiers.</summary>
    internal static readonly DiagnosticId ConflictingModifiers = new("MIST_DROP_CONFLICTING_MODIFIERS");

    /// <summary>A <c>Drop()</c> method declared with one or more parameters.</summary>
    internal static readonly DiagnosticId DropParameters = new("MIST_DROP_PARAMETERS");

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Declaration-level validations.
        ValidateDeclarations(model, diagnostics);

        // Flow-sensitive drop-state tracking (this task).
        AnalyzeFlowSensitive(model, diagnostics);
    }

    // ── Declaration-level validations ────────────────────────────────────

    /// <summary>
    /// Validates type and method declarations for drop-semantics rules:
    /// <list type="bullet">
    ///   <item>Conflicting substructural/ownership modifiers on a type.</item>
    ///   <item><c>Drop()</c> methods declared with parameters.</item>
    ///   <item>Non-owned types containing affine or linear fields.</item>
    /// </list>
    /// </summary>
    private static void ValidateDeclarations(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.Origin != SymbolOrigin.UserCode)
                continue;

            if (symbol.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(symbol); }
            catch { continue; }

            var modifiers = declaringNode switch
            {
                ClassDeclarationNode classDecl => classDecl.Modifiers,
                StructDeclarationNode structDecl => structDecl.Modifiers,
                _ => (ImmutableArray<ModifierNode>?)null,
            };

            // Validation 1: conflicting modifiers (MIST_DROP_CONFLICTING_MODIFIERS).
            if (modifiers is { } mods && HasConflictingModifiers(mods))
            {
                diagnostics.Add(MakeDiagnostic(
                    ConflictingModifiers,
                    "Conflicting type modifiers.",
                    declaringNode,
                    model));
            }

            // Validation 2: Drop() with parameters (MIST_DROP_PARAMETERS).
            ValidateDropParameters(symbol, model, diagnostics);

            // Validation 3: non-owned container with affine/linear field (MIST_DROP_FIELD_NOT_OWNED).
            ValidateFieldContainment(symbol, model, diagnostics);
        }
    }

    /// <summary>
    /// Determines whether a type's modifier list contains a conflicting combination of
    /// substructural/ownership modifiers:
    /// <list type="bullet">
    ///   <item>More than one of <c>affine</c>/<c>linear</c>/<c>relevant</c>.</item>
    ///   <item><c>owned</c> combined with any of <c>affine</c>/<c>linear</c>/<c>relevant</c>.</item>
    /// </list>
    /// </summary>
    private static bool HasConflictingModifiers(ImmutableArray<ModifierNode> modifiers)
    {
        var hasAffine = false;
        var hasLinear = false;
        var hasRelevant = false;
        var hasOwned = false;

        foreach (var modifier in modifiers)
        {
            switch (modifier)
            {
                case AffineModifierModifierNode:
                    hasAffine = true;
                    break;
                case LinearModifierModifierNode:
                    hasLinear = true;
                    break;
                case RelevantModifierModifierNode:
                    hasRelevant = true;
                    break;
                case OwnedModifierModifierNode:
                    hasOwned = true;
                    break;
            }
        }

        var substructuralCount = (hasAffine ? 1 : 0) + (hasLinear ? 1 : 0) + (hasRelevant ? 1 : 0);

        // More than one substructural modifier (e.g. affine linear, linear relevant).
        if (substructuralCount > 1)
            return true;

        // owned combined with any substructural modifier
        // (owned affine / owned linear are redundant, owned relevant is contradictory).
        if (hasOwned && substructuralCount >= 1)
            return true;

        return false;
    }

    /// <summary>
    /// Reports <c>MIST_DROP_PARAMETERS</c> for any method named <c>Drop</c> declared with
    /// one or more parameters on the given class or struct.
    /// </summary>
    private static void ValidateDropParameters(ISymbol typeSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var memberScope = GetMemberScope(typeSymbol.DeclaredType);
        if (memberScope is null)
            return;

        foreach (var member in memberScope.Members)
        {
            if (member.Kind != SymbolKind.Method || member.Name != "Drop")
                continue;

            SyntaxNode memberNode;
            try { memberNode = model.GetDeclaringNode(member); }
            catch { continue; }

            if (memberNode is not MethodDeclarationNode methodDecl)
                continue;

            if (CountParameters(methodDecl.Parameters) == 0)
                continue;

            diagnostics.Add(MakeDiagnostic(
                DropParameters,
                "Drop() method must not accept parameters.",
                memberNode,
                model));
        }
    }

    /// <summary>
    /// Validates the field-containment rule (Requirement 11.1): if a field's type has an
    /// <c>Affine</c> or <c>Linear</c> discipline, the container must be an owned type.
    /// Reports <c>MIST_DROP_FIELD_NOT_OWNED</c> otherwise.
    /// Relevant and unrestricted fields are always permitted.
    /// </summary>
    private static void ValidateFieldContainment(ISymbol typeSymbol, ISemanticModel model, DiagnosticBag diagnostics)
    {
        var containerType = typeSymbol.DeclaredType;
        if (containerType is null)
            return;

        var memberScope = GetMemberScope(containerType);
        if (memberScope is null)
            return;

        var containerIsOwned = IsOwnedType(containerType);

        foreach (var member in memberScope.Members)
        {
            if (member.Kind != SymbolKind.Field)
                continue;

            var fieldType = member.DeclaredType;
            if (fieldType is null)
                continue;

            var discipline = GetDiscipline(fieldType);
            if (discipline is not (SubstructuralDiscipline.Affine or SubstructuralDiscipline.Linear))
                continue;

            // Affine/linear field requires an owned container.
            if (containerIsOwned)
                continue;

            SyntaxNode fieldNode;
            try { fieldNode = model.GetDeclaringNode(member); }
            catch
            {
                try { fieldNode = model.GetDeclaringNode(typeSymbol); }
                catch { continue; }
            }

            var modifierName = discipline == SubstructuralDiscipline.Affine ? "affine" : "linear";

            diagnostics.Add(MakeDiagnostic(
                FieldNotOwned,
                $"Non-owned type '{typeSymbol.Name}' cannot contain {modifierName} field '{member.Name}'. " +
                "Aliasing the container would violate the single-holder invariant of the field.",
                fieldNode,
                model));
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the member scope of a class or struct type, or null otherwise.
    /// </summary>
    private static IScope? GetMemberScope(IType? type)
    {
        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            _ => null,
        };
    }

    /// <summary>
    /// Returns the substructural discipline of a class or struct type, or
    /// <see cref="SubstructuralDiscipline.None"/> otherwise.
    /// </summary>
    private static SubstructuralDiscipline GetDiscipline(IType type)
    {
        var resolved = type is ConstructedType constructed ? constructed.GenericDefinition : type;

        return resolved switch
        {
            ClassType ct => ct.Discipline,
            StructType st => st.Discipline,
            _ => SubstructuralDiscipline.None,
        };
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
            _ => false,
        };
    }

    /// <summary>
    /// Counts the parameters declared in a parameter list node.
    /// </summary>
    private static int CountParameters(ParameterListNode? paramList)
    {
        if (paramList is null)
            return 0;

        var count = 0;
        foreach (var child in paramList.Children())
        {
            if (child is ParameterNode)
                count++;
        }
        return count;
    }

    /// <summary>
    /// Creates a diagnostic with source location information resolved from the AST node.
    /// Mirrors the <see cref="OwnershipAnalyzer"/> helper of the same name.
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
            AnalyzerName: "Drop");
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Flow-sensitive drop-state tracking (Task 5.2)
    // Requirements: 3.1, 3.2, 3.3, 6.2, 6.3, 12.1, 12.2, 12.3, 15.1, 15.2, 15.4
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Walks every user-code method and constructor body flow-sensitively, tracking the
    /// <see cref="VariableDropState"/> of each affine/linear local and parameter. Reuses the
    /// <see cref="MethodFlowState"/> Fork/Merge infrastructure (shared with the
    /// <see cref="OwnershipAnalyzer"/>) so branch-sensitive consumption is tracked correctly.
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
    /// Analyzes a single method or constructor body. Registers affine/linear parameters as
    /// <see cref="VariableDropState.Live"/> and walks the body block. The
    /// <paramref name="isDropMethod"/> flag determines whether field <c>Drop()</c> calls on
    /// <c>this</c> are permitted (Requirement 12.2).
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

        // A method named Drop with zero parameters is the type's own Drop() method;
        // field drops on `this` are permitted only here (Requirement 12.2).
        var isDropMethod =
            methodSymbol.Kind == SymbolKind.Method &&
            methodSymbol.Name == "Drop" &&
            CountMethodParameters(declaringNode) == 0;

        var state = new MethodFlowState();

        RegisterDroppableParameters(declaringNode, model, state);

        WalkBlock(body, state, model, diagnostics, isDropMethod);
    }

    /// <summary>
    /// Registers all affine/linear parameters of a method/constructor as
    /// <see cref="VariableDropState.Live"/> in the flow state.
    /// </summary>
    private static void RegisterDroppableParameters(SyntaxNode declaringNode, ISemanticModel model, MethodFlowState state)
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
            if (child is not ParameterNode paramNode)
                continue;

            var paramSymbol = model.GetDeclaredSymbol(paramNode);
            if (paramSymbol?.DeclaredType is null)
                continue;

            if (IsOwnedDroppable(GetDiscipline(paramSymbol.DeclaredType)))
                state.DropStates[paramSymbol] = VariableDropState.Live;
        }
    }

    // ── Statement walk ───────────────────────────────────────────────────

    /// <summary>
    /// Walks a block of statements in order, threading the flow state through each.
    /// </summary>
    private static void WalkBlock(
        BlockNode block,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        foreach (var stmt in block.Statements)
        {
            WalkStatement(stmt, state, model, diagnostics, isDropMethod);
        }
    }

    /// <summary>
    /// Walks a single statement, dispatching on its kind. Control-flow statements fork the
    /// state at branch points and merge at join points so consumption is branch-sensitive.
    /// </summary>
    private static void WalkStatement(
        StatementNode stmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        switch (stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                AnalyzeVariableDeclaration(varDeclStmt.VariableDeclaration, state, model, diagnostics, isDropMethod);
                break;

            case ExpressionStatementStatementNode exprStmt:
                AnalyzeExpressionStatement(exprStmt.ExpressionStatement.Expression, state, model, diagnostics, isDropMethod);
                break;

            case IfStatementStatementNode ifStmt:
                AnalyzeIfStatement(ifStmt.IfStatement, state, model, diagnostics, isDropMethod);
                break;

            case WhileStatementStatementNode whileStmt:
                ProcessExpression(whileStmt.WhileStatement.Condition, state, model, diagnostics, isDropMethod);
                WalkBlock(whileStmt.WhileStatement.Body, state, model, diagnostics, isDropMethod);
                break;

            case ForStatementStatementNode forStmt:
                AnalyzeForStatement(forStmt.ForStatement, state, model, diagnostics, isDropMethod);
                break;

            case ForeachStatementStatementNode foreachStmt:
                ProcessExpression(foreachStmt.ForeachStatement.Collection, state, model, diagnostics, isDropMethod);
                WalkBlock(foreachStmt.ForeachStatement.Body, state, model, diagnostics, isDropMethod);
                break;

            case ReturnStatementStatementNode returnStmt:
                AnalyzeReturnStatement(returnStmt.ReturnStatement, state, model, diagnostics, isDropMethod);
                break;

            case ReadonlyBlockStatementStatementNode readonlyBlockStmt:
                WalkBlock(readonlyBlockStmt.ReadonlyBlock.Body, state, model, diagnostics, isDropMethod);
                break;

            case BlockStatementStatementNode blockStmt:
                WalkBlock(blockStmt.BlockStatement, state, model, diagnostics, isDropMethod);
                break;

            case WithBlockStatementStatementNode withBlockStmt:
                ProcessExpression(withBlockStmt.WithBlockStatement.Expression, state, model, diagnostics, isDropMethod);
                WalkBlock(withBlockStmt.WithBlockStatement.Body, state, model, diagnostics, isDropMethod);
                break;

            default:
                // Other statement forms: scan any directly-contained expressions for uses,
                // drop consumes, and field-drop violations.
                foreach (var child in stmt.Children())
                {
                    if (child is ExpressionNode childExpr)
                        ProcessExpression(childExpr, state, model, diagnostics, isDropMethod);
                }
                break;
        }
    }

    /// <summary>
    /// Analyzes a variable declaration: processes the initializer (uses, drop consumes,
    /// field-drop checks), applies an init move when the initializer is a droppable variable,
    /// then registers the declared variable as <see cref="VariableDropState.Live"/> when it is
    /// an affine/linear local.
    /// </summary>
    private static void AnalyzeVariableDeclaration(
        VariableDeclarationStatementNode varDecl,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        ExpressionNode? initializer = varDecl switch
        {
            TypedVariableDeclarationVariableDeclarationStatementNode typed => typed.Initializer,
            InferredVariableDeclarationVariableDeclarationStatementNode inferred => inferred.Initializer,
            _ => null,
        };

        if (initializer is not null)
        {
            ProcessExpression(initializer, state, model, diagnostics, isDropMethod);

            // Initializing one droppable binding from another moves the source.
            var sourceSymbol = TryResolveIdentifierSymbol(initializer, model);
            if (sourceSymbol is not null && IsTrackedDroppable(sourceSymbol, state))
                MarkConsumed(sourceSymbol, VariableDropState.Moved, state);
        }

        var declaredSymbol = model.GetDeclaredSymbol(varDecl);
        if (declaredSymbol?.DeclaredType is not null &&
            declaredSymbol.Kind == SymbolKind.LocalVariable &&
            IsOwnedDroppable(GetDiscipline(declaredSymbol.DeclaredType)))
        {
            state.DropStates[declaredSymbol] = VariableDropState.Live;
        }
    }

    /// <summary>
    /// Analyzes a top-level expression statement. Processes uses/consumes/field-drops, then
    /// applies a move when the statement is a simple <c>target = source</c> assignment whose
    /// source is a droppable variable.
    /// </summary>
    private static void AnalyzeExpressionStatement(
        ExpressionNode expr,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        ProcessExpression(expr, state, model, diagnostics, isDropMethod);

        // Detect `target = source;` where source is a droppable variable → move source.
        if (expr.AsAssignmentOrNull() is { Value: not null } assignment &&
            assignment.Value.AsAssignmentOrNull() is { } rhs &&
            rhs.Value is null)
        {
            var sourceSymbol = TryResolveIdentifierSymbol(rhs.Target, model);
            if (sourceSymbol is not null && IsTrackedDroppable(sourceSymbol, state))
                MarkConsumed(sourceSymbol, VariableDropState.Moved, state);
        }
    }

    /// <summary>
    /// Analyzes an if/else statement by forking the flow state for each branch and merging the
    /// results at the join point (consumption in any branch is conservative — see
    /// <see cref="MethodFlowState.Merge"/>).
    /// </summary>
    private static void AnalyzeIfStatement(
        IfStatementNode ifStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        ProcessExpression(ifStmt.Condition, state, model, diagnostics, isDropMethod);

        // Fork: thenState carries the pre-if snapshot for the else branch.
        var thenState = state.Fork();

        state.EnterScope();
        WalkBlock(ifStmt.ThenBranch, state, model, diagnostics, isDropMethod);
        state.ExitScope();

        if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            thenState.EnterScope();
            WalkBlock(elseBlock.ElseBlock, thenState, model, diagnostics, isDropMethod);
            thenState.ExitScope();
            state.Merge(thenState);
        }
        else if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            AnalyzeIfStatement(elseIf.ElseIf, thenState, model, diagnostics, isDropMethod);
            state.Merge(thenState);
        }
        else
        {
            state.Merge(thenState);
        }
    }

    /// <summary>
    /// Analyzes a for statement, processing the initializer, condition, body, and iterator.
    /// </summary>
    private static void AnalyzeForStatement(
        ForStatementNode forStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        if (forStmt.Initializer is not null)
        {
            foreach (var child in forStmt.Initializer.Children())
            {
                if (child is ExpressionNode expr)
                    ProcessExpression(expr, state, model, diagnostics, isDropMethod);
            }
        }

        if (forStmt.Condition is not null)
            ProcessExpression(forStmt.Condition, state, model, diagnostics, isDropMethod);

        WalkBlock(forStmt.Body, state, model, diagnostics, isDropMethod);

        if (forStmt.Iterator is not null)
            ProcessExpression(forStmt.Iterator, state, model, diagnostics, isDropMethod);
    }

    /// <summary>
    /// Analyzes a return statement: processes the returned expression, then marks a returned
    /// droppable variable as <see cref="VariableDropState.Returned"/> (ownership transfers to
    /// the caller — Requirement 6.2).
    /// </summary>
    private static void AnalyzeReturnStatement(
        ReturnStatementNode returnStmt,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        if (returnStmt.Value is null)
            return;

        ProcessExpression(returnStmt.Value, state, model, diagnostics, isDropMethod);

        var sourceSymbol = TryResolveIdentifierSymbol(returnStmt.Value, model);
        if (sourceSymbol is not null && IsTrackedDroppable(sourceSymbol, state))
            MarkConsumed(sourceSymbol, VariableDropState.Returned, state);
    }

    // ── Expression processing ──────────────────────────────────────────────

    /// <summary>
    /// Processes an expression in three ordered passes against the pre-statement state:
    /// <list type="number">
    ///   <item>Report uses of already-consumed droppable variables (use-after-drop/move).</item>
    ///   <item>Apply <c>v.Drop()</c> consumption for affine/linear variables.</item>
    ///   <item>Validate field <c>Drop()</c> calls against the container restriction.</item>
    /// </list>
    /// Reporting runs before consumption so the consuming <c>Drop()</c> call is not flagged as a
    /// use of the value it consumes, while a redundant drop of an already-consumed value is.
    /// </summary>
    private static void ProcessExpression(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        ReportUsesOfConsumedVars(node, state, model, diagnostics);
        ApplyDropConsumes(node, state, model);
        CheckFieldDrops(node, state, model, diagnostics, isDropMethod);
    }

    /// <summary>
    /// Recursively reports a use-after-drop/move for every identifier reference to a tracked
    /// droppable variable that is currently in a consumed state. Linear variables report
    /// <c>MIST_DROP_USE_AFTER_MOVE</c>; affine variables report <c>MIST_DROP_USE_AFTER_DROP</c>.
    /// </summary>
    private static void ReportUsesOfConsumedVars(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (node is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if (symbol is not null &&
                state.DropStates.TryGetValue(symbol, out var dropState) &&
                dropState != VariableDropState.Live)
            {
                var discipline = symbol.DeclaredType is not null
                    ? GetDiscipline(symbol.DeclaredType)
                    : SubstructuralDiscipline.None;

                if (discipline == SubstructuralDiscipline.Affine)
                {
                    diagnostics.Add(MakeDiagnostic(
                        UseAfterDrop,
                        $"Use of dropped value '{symbol.Name}'.",
                        identifier,
                        model));
                }
                else
                {
                    diagnostics.Add(MakeDiagnostic(
                        UseAfterMove,
                        $"Use of moved/dropped value '{symbol.Name}'.",
                        identifier,
                        model));
                }
            }
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                ReportUsesOfConsumedVars(child, state, model, diagnostics);
        }
    }

    /// <summary>
    /// Recursively applies <c>v.Drop()</c> consumption: when a bare droppable variable's
    /// <c>Drop()</c> method is called and the variable is currently Live, it transitions to
    /// <see cref="VariableDropState.Dropped"/>. Relevant types are not tracked and therefore
    /// never consumed (Requirement 3.3).
    /// </summary>
    private static void ApplyDropConsumes(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model)
    {
        if (node is PostfixNode postfix && TryGetVariableDropTarget(postfix, model) is { } varSymbol)
        {
            if (IsTrackedDroppable(varSymbol, state) &&
                state.DropStates[varSymbol] == VariableDropState.Live)
            {
                state.DropStates[varSymbol] = VariableDropState.Dropped;
            }
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                ApplyDropConsumes(child, state, model);
        }
    }

    /// <summary>
    /// Recursively validates field <c>Drop()</c> calls. A call of the form
    /// <c>receiver.Field.Drop()</c> on an affine/linear field is permitted only when it is a
    /// direct <c>this.Field.Drop()</c> inside the containing type's own <c>Drop()</c> method;
    /// otherwise <c>MIST_DROP_FIELD_OUTSIDE_CONTAINER</c> is reported. Relevant fields are
    /// exempt (Requirement 12.3).
    /// </summary>
    private static void CheckFieldDrops(
        SyntaxNode node,
        MethodFlowState state,
        ISemanticModel model,
        DiagnosticBag diagnostics,
        bool isDropMethod)
    {
        if (node is PostfixNode postfix && postfix.Suffix.Length >= 2)
        {
            var last = postfix.Suffix[^1];
            var prev = postfix.Suffix[^2];

            if (last is MethodCallSuffixPostfixSuffixNode dropCall &&
                dropCall.Name.Lexeme == "Drop" &&
                CountArguments(dropCall.Arguments) == 0 &&
                prev is MemberAccessSuffixPostfixSuffixNode fieldAccess)
            {
                var fieldType = model.GetTypeOf(prev);
                var discipline = fieldType is not null ? GetDiscipline(fieldType) : SubstructuralDiscipline.None;

                if (IsOwnedDroppable(discipline))
                {
                    // Permitted only for `this.Field.Drop()` inside the type's own Drop().
                    var permitted =
                        isDropMethod &&
                        postfix.Suffix.Length == 2 &&
                        postfix.Target is ThisExpressionPrimaryNode;

                    if (!permitted)
                    {
                        diagnostics.Add(MakeDiagnostic(
                            FieldOutsideContainer,
                            $"Cannot drop field '{fieldAccess.Name.Lexeme}' outside of the containing type's Drop() method.",
                            prev,
                            model));
                    }
                }
            }
        }

        foreach (var child in node.Children())
        {
            if (child is not null)
                CheckFieldDrops(child, state, model, diagnostics, isDropMethod);
        }
    }

    // ── Flow-sensitive helpers ─────────────────────────────────────────────

    /// <summary>
    /// Returns the local/parameter symbol of a bare <c>v.Drop()</c> variable-drop call, or
    /// <c>null</c> when the postfix is not a single zero-argument <c>Drop()</c> call on a bare
    /// identifier.
    /// </summary>
    private static ISymbol? TryGetVariableDropTarget(PostfixNode postfix, ISemanticModel model)
    {
        if (postfix.Suffix.Length != 1)
            return null;

        if (postfix.Suffix[0] is not MethodCallSuffixPostfixSuffixNode methodCall)
            return null;

        if (methodCall.Name.Lexeme != "Drop" || CountArguments(methodCall.Arguments) != 0)
            return null;

        if (postfix.Target is not IdentifierExpressionPrimaryNode identifier)
            return null;

        var symbol = model.GetDeclaredSymbol(identifier);
        if (symbol is null || symbol.Kind is not (SymbolKind.LocalVariable or SymbolKind.Parameter))
            return null;

        return symbol;
    }

    /// <summary>
    /// Marks a tracked droppable variable as consumed with the given state, leaving an already
    /// non-Live variable unchanged (the first consumption wins for diagnostic purposes).
    /// </summary>
    private static void MarkConsumed(ISymbol symbol, VariableDropState consumed, MethodFlowState state)
    {
        if (state.DropStates.TryGetValue(symbol, out var current) && current == VariableDropState.Live)
            state.DropStates[symbol] = consumed;
    }

    /// <summary>
    /// Returns <c>true</c> if the symbol is currently tracked in the flow state's drop states
    /// (i.e., it is an affine/linear local or parameter registered for drop tracking).
    /// </summary>
    private static bool IsTrackedDroppable(ISymbol symbol, MethodFlowState state)
    {
        return state.DropStates.ContainsKey(symbol);
    }

    /// <summary>
    /// Returns <c>true</c> for the owned droppable disciplines (affine/linear) whose values are
    /// consumed by <c>Drop()</c>. Relevant and unrestricted types are not consumed.
    /// </summary>
    private static bool IsOwnedDroppable(SubstructuralDiscipline discipline)
    {
        return discipline is SubstructuralDiscipline.Affine or SubstructuralDiscipline.Linear;
    }

    /// <summary>
    /// Resolves an expression to a single bare identifier symbol referencing a local variable
    /// or parameter, unwrapping the expression chain. Returns <c>null</c> for anything that is
    /// not a simple identifier reference.
    /// </summary>
    private static ISymbol? TryResolveIdentifierSymbol(SyntaxNode node, ISemanticModel model)
    {
        switch (node)
        {
            case ExpressionNode expr:
                return expr.AsAssignmentOrNull() is { } exprAssignment
                    ? TryResolveIdentifierSymbol(exprAssignment, model)
                    : null;

            case AssignmentNode assignment:
                return assignment.Value is not null
                    ? null
                    : TryResolveIdentifierSymbol(assignment.Target, model);

            case PostfixNode postfix:
                return postfix.Suffix.Length > 0
                    ? null
                    : TryResolveIdentifierSymbol(postfix.Target, model);

            case IdentifierExpressionPrimaryNode identifier:
            {
                var symbol = model.GetDeclaredSymbol(identifier);
                return symbol is not null &&
                       symbol.Kind is SymbolKind.LocalVariable or SymbolKind.Parameter
                    ? symbol
                    : null;
            }
        }

        SyntaxNode? onlyChild = null;
        var childCount = 0;
        foreach (var child in node.Children())
        {
            if (child is null)
                continue;
            childCount++;
            onlyChild = child;
            if (childCount > 1)
                return null;
        }

        return childCount == 1 && onlyChild is not null
            ? TryResolveIdentifierSymbol(onlyChild, model)
            : null;
    }

    /// <summary>
    /// Counts the parameters declared on a method/constructor declaration node.
    /// </summary>
    private static int CountMethodParameters(SyntaxNode declaringNode)
    {
        ParameterListNode? paramList = declaringNode switch
        {
            MethodDeclarationNode m => m.Parameters,
            ConstructorDeclarationNode c => c.Parameters,
            _ => null,
        };

        return CountParameters(paramList);
    }

    /// <summary>
    /// Counts the value arguments in an argument list node.
    /// </summary>
    private static int CountArguments(ArgumentListNode? argList)
    {
        return argList?.GetAllExpressions().Length ?? 0;
    }
}
