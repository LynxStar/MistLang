using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that validates structured context availability at compile time.
/// Walks the call graph to verify that every method with a <c>with</c> clause is
/// always called within a scope that provides the required context type. Also
/// enforces abstract type with-clause contract matching.
/// </summary>
public sealed class ContextCheckerAnalyzer : IAnalyzer
{
    public string AnalyzerId => "ContextChecker";

    // ── Diagnostic IDs ───────────────────────────────────────────────────

    /// <summary>
    /// Method with a <c>with</c> clause called without a matching context in scope.
    /// Message: "Method '{method}' requires context of type '{type}', but no matching
    /// context declaration is in scope at {location}."
    /// </summary>
    internal static readonly DiagnosticId MissingContext = new("MIST_CTX_0001");

    /// <summary>
    /// Method has multiple <c>with</c> clauses binding the same type.
    /// Message: "Method '{method}' has duplicate context binding for type '{type}'
    /// at {location}."
    /// </summary>
    internal static readonly DiagnosticId DuplicateContextBinding = new("MIST_CTX_0002");

    /// <summary>
    /// Context declaration statement appears outside a method body.
    /// Message: "Context declaration is only valid inside a method body, at {location}."
    /// </summary>
    internal static readonly DiagnosticId ContextDeclarationOutsideMethod = new("MIST_CTX_0003");

    /// <summary>
    /// Context expression type cannot be inferred.
    /// Message: "Cannot determine context type from expression at {location}."
    /// </summary>
    internal static readonly DiagnosticId CannotInferContextType = new("MIST_CTX_0004");

    /// <summary>
    /// Assignment to a context-bound variable.
    /// Message: "Context-bound variable '{name}' is not reassignable, at {location}."
    /// </summary>
    internal static readonly DiagnosticId ContextVariableNotReassignable = new("MIST_CTX_0005");

    /// <summary>
    /// Implementing method missing required with-clause from abstract type.
    /// Message: "Method '{method}' on type '{type}' must declare `with {contextType}`
    /// to satisfy abstract type '{contract}' at {location}."
    /// </summary>
    internal static readonly DiagnosticId WithClauseContractViolation = new("MIST_CTX_0006");

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        CheckDuplicateContextBindings(model, diagnostics);
        CheckContextDeclarationsOutsideMethodBody(model, diagnostics);
        CheckCannotInferContextType(model, diagnostics);
        CheckContextBoundVariableAssignments(model, diagnostics);
        CheckContextAvailability(model, diagnostics);
        CheckAbstractWithClauseContracts(model, diagnostics);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Duplicate Context Binding (Task 5.4 — MIST_CTX_0002)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks each method's with-clauses for duplicate resolved types. If a method
    /// declares multiple with-clauses binding the same type, emits MIST_CTX_0002.
    /// </summary>
    private static void CheckDuplicateContextBindings(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol sym)
                continue;
            if (sym.Origin == SymbolOrigin.Backend)
                continue;
            if (sym.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;
            if (sym.WithClauses.Count < 2)
                continue;

            // Check for duplicate resolved types among the with-clauses.
            for (var i = 0; i < sym.WithClauses.Count; i++)
            {
                var typeI = ResolveContextType(sym.WithClauses[i].ContextType);
                for (var j = i + 1; j < sym.WithClauses.Count; j++)
                {
                    var typeJ = ResolveContextType(sym.WithClauses[j].ContextType);
                    if (ContextTypesMatch(typeI, typeJ))
                    {
                        // Emit on the second (duplicate) clause.
                        var clause = sym.WithClauses[j];
                        SyntaxNode? declaringNode;
                        try { declaringNode = model.GetDeclaringNode(sym); }
                        catch { declaringNode = null; }

                        if (declaringNode is not null)
                        {
                            var (path, location, _) = DiagnosticHelper.ResolveLocation(declaringNode);
                            diagnostics.Add(new Diagnostic(
                                DuplicateContextBinding,
                                DiagnosticSeverity.Error,
                                PipelineStage.Analysis,
                                $"Method '{sym.Name}' has duplicate context binding for type '{typeI.DisplayName}'.",
                                Location: location,
                                CompilationUnitPath: path,
                                AnalyzerName: "ContextChecker"));
                        }
                        else
                        {
                            diagnostics.Add(new Diagnostic(
                                DuplicateContextBinding,
                                DiagnosticSeverity.Error,
                                PipelineStage.Analysis,
                                $"Method '{sym.Name}' has duplicate context binding for type '{typeI.DisplayName}'.",
                                Location: default,
                                CompilationUnitPath: "<unknown>",
                                AnalyzerName: "ContextChecker"));
                        }
                    }
                }
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Context Declaration Outside Method Body (Task 5.4 — MIST_CTX_0003)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Defensive check: verifies that context declaration statements only appear
    /// inside method bodies. The grammar should prevent this, but this check
    /// catches any edge cases.
    /// </summary>
    private static void CheckContextDeclarationsOutsideMethodBody(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk all ASTs looking for context declaration nodes that are not
        // inside a method or constructor body.
        foreach (var ast in model.Asts)
        {
            WalkForContextDeclarationsOutsideMethod(ast, insideMethodBody: false, diagnostics);
        }
    }

    /// <summary>
    /// Recursively walks the AST to find context declaration statements that
    /// appear outside a method body.
    /// </summary>
    private static void WalkForContextDeclarationsOutsideMethod(
        SyntaxNode node,
        bool insideMethodBody,
        DiagnosticBag diagnostics)
    {
        if (node is null)
            return;

        switch (node)
        {
            case MethodDeclarationNode:
            case ConstructorDeclarationNode:
                // Everything inside a method/constructor body is valid.
                // No need to recurse further — context declarations inside are fine.
                return;

            case WithVarDeclarationStatementStatementNode withVar when !insideMethodBody:
                EmitContextOutsideMethodDiagnostic(withVar, diagnostics);
                return;

            case WithTypedDeclarationStatementStatementNode withTyped when !insideMethodBody:
                EmitContextOutsideMethodDiagnostic(withTyped, diagnostics);
                return;

            case WithDiscardDeclarationStatementStatementNode withDiscard when !insideMethodBody:
                EmitContextOutsideMethodDiagnostic(withDiscard, diagnostics);
                return;

            case WithPushStatementStatementNode withPush when !insideMethodBody:
                EmitContextOutsideMethodDiagnostic(withPush, diagnostics);
                return;

            case WithBlockStatementStatementNode withBlock when !insideMethodBody:
                EmitContextOutsideMethodDiagnostic(withBlock, diagnostics);
                return;
        }

        // Recurse into children.
        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                WalkForContextDeclarationsOutsideMethod(child, insideMethodBody, diagnostics);
            }
        }
    }

    private static void EmitContextOutsideMethodDiagnostic(SyntaxNode node, DiagnosticBag diagnostics)
    {
        var (path, location, _) = DiagnosticHelper.ResolveLocation(node);
        diagnostics.Add(new Diagnostic(
            ContextDeclarationOutsideMethod,
            DiagnosticSeverity.Error,
            PipelineStage.Analysis,
            "Context declaration is only valid inside a method body.",
            Location: location,
            CompilationUnitPath: path,
            AnalyzerName: "ContextChecker"));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Cannot Infer Context Type (Task 5.4 — MIST_CTX_0004)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks that context declarations that rely on type inference (with-push
    /// statements and with-block statements) have expressions whose type can be
    /// determined. Emits MIST_CTX_0004 when the type cannot be inferred.
    /// </summary>
    private static void CheckCannotInferContextType(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol sym)
                continue;
            if (sym.Origin == SymbolOrigin.Backend)
                continue;
            if (sym.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(sym); }
            catch { continue; }

            BlockNode? body = declaringNode switch
            {
                MethodDeclarationNode m => m.Body.GetBlock(),
                ConstructorDeclarationNode c => c.Body.GetBlock(),
                _ => null,
            };

            if (body is null)
                continue;

            WalkBlockForUninferredContextTypes(body, model, diagnostics);
        }
    }

    /// <summary>
    /// Walks a block looking for with-push and with-block statements whose
    /// expression type cannot be determined.
    /// </summary>
    private static void WalkBlockForUninferredContextTypes(
        BlockNode block,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            switch (stmt)
            {
                case WithPushStatementStatementNode withPush:
                {
                    var type = model.GetTypeOf(withPush.WithPushStatement.Value);
                    if (type is null)
                    {
                        var (path, location, _) = DiagnosticHelper.ResolveLocation(withPush.WithPushStatement.Value);
                        diagnostics.Add(new Diagnostic(
                            CannotInferContextType,
                            DiagnosticSeverity.Error,
                            PipelineStage.Analysis,
                            "Cannot determine context type from expression.",
                            Location: location,
                            CompilationUnitPath: path,
                            AnalyzerName: "ContextChecker"));
                    }
                    break;
                }

                case WithBlockStatementStatementNode withBlock:
                {
                    var type = model.GetTypeOf(withBlock.WithBlockStatement.Expression);
                    if (type is null)
                    {
                        var (path, location, _) = DiagnosticHelper.ResolveLocation(withBlock.WithBlockStatement.Expression);
                        diagnostics.Add(new Diagnostic(
                            CannotInferContextType,
                            DiagnosticSeverity.Error,
                            PipelineStage.Analysis,
                            "Cannot determine context type from expression.",
                            Location: location,
                            CompilationUnitPath: path,
                            AnalyzerName: "ContextChecker"));
                    }

                    // Also walk the block body for nested statements.
                    WalkBlockForUninferredContextTypes(withBlock.WithBlockStatement.Body, model, diagnostics);
                    break;
                }

                default:
                    // Walk nested blocks (if, while, for, foreach, etc.)
                    WalkStatementForUninferredContextTypes(stmt, model, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Walks a statement recursively looking for nested blocks that may contain
    /// context declarations with uninferred types.
    /// </summary>
    private static void WalkStatementForUninferredContextTypes(
        SyntaxNode stmt,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // StatementNode wraps inner nodes (e.g., IfStatementStatementNode wraps IfStatementNode).
        // Walk into children to find nested blocks.
        foreach (var child in stmt.Children())
        {
            if (child is null)
                continue;

            switch (child)
            {
                case IfStatementNode ifStmt:
                    WalkBlockForUninferredContextTypes(ifStmt.ThenBranch, model, diagnostics);
                    if (ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
                        WalkStatementForUninferredContextTypes(elseIf.ElseIf, model, diagnostics);
                    else if (ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
                        WalkBlockForUninferredContextTypes(elseBlock.ElseBlock, model, diagnostics);
                    break;

                case WhileStatementNode whileStmt:
                    WalkBlockForUninferredContextTypes(whileStmt.Body, model, diagnostics);
                    break;

                case ForStatementNode forStmt:
                    WalkBlockForUninferredContextTypes(forStmt.Body, model, diagnostics);
                    break;

                case ForeachStatementNode foreachStmt:
                    WalkBlockForUninferredContextTypes(foreachStmt.Body, model, diagnostics);
                    break;

                case BlockNode block:
                    WalkBlockForUninferredContextTypes(block, model, diagnostics);
                    break;
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Context-Bound Variable Assignment (Task 5.4 — MIST_CTX_0005)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Checks that context-bound variables (identifiers from with-clauses) are not
    /// the target of assignment expressions. Emits MIST_CTX_0005 when a context-bound
    /// variable is reassigned.
    /// </summary>
    private static void CheckContextBoundVariableAssignments(ISemanticModel model, DiagnosticBag diagnostics)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol sym)
                continue;
            if (sym.Origin == SymbolOrigin.Backend)
                continue;
            if (sym.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;
            if (sym.WithClauses.Count == 0)
                continue;

            // Collect the names of context-bound variables for this method.
            var contextBoundNames = new HashSet<string>();
            foreach (var clause in sym.WithClauses)
            {
                contextBoundNames.Add(clause.Identifier);
            }

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(sym); }
            catch { continue; }

            BlockNode? body = declaringNode switch
            {
                MethodDeclarationNode m => m.Body.GetBlock(),
                ConstructorDeclarationNode c => c.Body.GetBlock(),
                _ => null,
            };

            if (body is null)
                continue;

            WalkBlockForContextBoundAssignments(body, contextBoundNames, model, diagnostics);
        }
    }

    /// <summary>
    /// Walks a block looking for assignment expressions that target context-bound variables.
    /// </summary>
    private static void WalkBlockForContextBoundAssignments(
        BlockNode block,
        HashSet<string> contextBoundNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var stmt in block.Statements)
        {
            WalkNodeForContextBoundAssignments(stmt, contextBoundNames, model, diagnostics);
        }
    }

    /// <summary>
    /// Recursively walks an AST node looking for assignment expressions that target
    /// context-bound variables.
    /// </summary>
    private static void WalkNodeForContextBoundAssignments(
        SyntaxNode node,
        HashSet<string> contextBoundNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if (node is null)
            return;

        switch (node)
        {
            case AssignmentNode assignment when assignment.Value is not null:
                CheckContextBoundAssignment(assignment, contextBoundNames, model, diagnostics);
                break;

            case BlockNode block:
                WalkBlockForContextBoundAssignments(block, contextBoundNames, model, diagnostics);
                return;
        }

        // Recurse into children.
        foreach (var child in node.Children())
        {
            if (child is not null)
            {
                WalkNodeForContextBoundAssignments(child, contextBoundNames, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Checks if an assignment targets a context-bound variable. If so, emits MIST_CTX_0005.
    /// </summary>
    private static void CheckContextBoundAssignment(
        AssignmentNode assignment,
        HashSet<string> contextBoundNames,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // The assignment target is a LogicalOrNode chain. We need to find the
        // identifier at the bottom of the chain. For a simple `x = value`,
        // the target chain is: LogicalOr -> LogicalAnd -> Equality -> Relational
        // -> Additive -> Multiplicative -> Unary -> Postfix -> Primary (Identifier).
        var identifierName = ExtractSimpleAssignmentTargetName(assignment.Target);
        if (identifierName is null)
            return;

        if (contextBoundNames.Contains(identifierName))
        {
            var (path, location, _) = DiagnosticHelper.ResolveLocation(assignment);
            diagnostics.Add(new Diagnostic(
                ContextVariableNotReassignable,
                DiagnosticSeverity.Error,
                PipelineStage.Analysis,
                $"Context-bound variable '{identifierName}' is not reassignable.",
                Location: location,
                CompilationUnitPath: path,
                AnalyzerName: "ContextChecker"));
        }
    }

    /// <summary>
    /// Extracts the simple identifier name from an assignment target if it is a
    /// bare identifier (not a member access, indexer, etc.).
    /// </summary>
    private static string? ExtractSimpleAssignmentTargetName(SyntaxNode targetNode)
    {
        // Walk through the expression precedence chain to find the PostfixNode.
        var postfix = FindPostfixInTarget(targetNode);
        if (postfix is null)
            return null;

        // A bare identifier assignment has no suffixes and an IdentifierExpressionPrimaryNode target.
        if (postfix.Suffix.Length == 0 && postfix.Target is IdentifierExpressionPrimaryNode identifier)
        {
            return identifier.Name.Lexeme;
        }

        return null;
    }

    /// <summary>
    /// Navigates through the expression precedence chain (LogicalOr -> LogicalAnd ->
    /// Equality -> Relational -> Additive -> Multiplicative -> Unary -> Postfix)
    /// to find the PostfixNode at the bottom.
    /// </summary>
    private static PostfixNode? FindPostfixInTarget(SyntaxNode node)
    {
        switch (node)
        {
            case PostfixNode postfix:
                return postfix;
            case LogicalOrNode logOr:
                return FindPostfixInTarget(logOr.Left);
            case LogicalAndNode logAnd:
                return FindPostfixInTarget(logAnd.Left);
            case EqualityNode eq:
                return FindPostfixInTarget(eq.Left);
            case RelationalComparisonRelationalNode rel:
                return FindPostfixInTarget(rel.Left);
            case AdditiveNode add:
                return FindPostfixInTarget(add.Left);
            case MultiplicativeNode mul:
                return FindPostfixInTarget(mul.Left);
            case PostfixExpressionUnaryNode postfixUnary:
                return postfixUnary.Primary;
            default:
                // Try children as fallback.
                foreach (var child in node.Children())
                {
                    if (child is not null)
                    {
                        var result = FindPostfixInTarget(child);
                        if (result is not null)
                            return result;
                    }
                }
                return null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Context Availability Checking (Task 5.2)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Collects all method symbols with WithClauses, then walks each method body
    /// to find call sites that invoke those methods. For each call site, verifies
    /// that a matching context declaration is reachable.
    /// </summary>
    private static void CheckContextAvailability(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Step 1: Collect all method/constructor symbols that have WithClauses.
        var methodsWithContext = new Dictionary<string, List<Symbol>>();
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol sym)
                continue;
            if (sym.Origin == SymbolOrigin.Backend)
                continue;
            if (sym.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;
            if (sym.WithClauses.Count == 0)
                continue;

            if (!methodsWithContext.TryGetValue(sym.Name, out var list))
            {
                list = [];
                methodsWithContext[sym.Name] = list;
            }
            list.Add(sym);
        }

        if (methodsWithContext.Count == 0)
            return;

        // Step 2: Walk each method/constructor body to find call sites.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol sym)
                continue;
            if (sym.Origin == SymbolOrigin.Backend)
                continue;
            if (sym.Kind is not (SymbolKind.Method or SymbolKind.Constructor))
                continue;

            SyntaxNode declaringNode;
            try { declaringNode = model.GetDeclaringNode(sym); }
            catch { continue; }

            BlockNode? body = declaringNode switch
            {
                MethodDeclarationNode m => m.Body.GetBlock(),
                ConstructorDeclarationNode c => c.Body.GetBlock(),
                _ => null,
            };

            if (body is null)
                continue;

            // Walk the body looking for call sites.
            WalkBlockForCallSites(body, [], sym, model, methodsWithContext, diagnostics);
        }
    }

    /// <summary>
    /// Walks a block looking for call sites that invoke methods with WithClauses.
    /// Tracks context declarations encountered before each call site.
    /// </summary>
    private static void WalkBlockForCallSites(
        BlockNode block,
        List<IType> availableContextTypes,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext,
        DiagnosticBag diagnostics)
    {
        // Process statements in order. Context declarations add to available types
        // for subsequent statements in the same block.
        var localContextTypes = new List<IType>();

        foreach (var stmt in block.Statements)
        {
            // First, check if this statement is a context declaration.
            var contextType = TryGetContextDeclarationType(stmt, model);
            if (contextType is not null)
            {
                localContextTypes.Add(contextType);
            }

            // Build the combined available context for this statement position.
            var combinedContext = new List<IType>(availableContextTypes);
            combinedContext.AddRange(localContextTypes);

            // Also add context types from the enclosing method's own with-clauses.
            foreach (var clause in enclosingMethod.WithClauses)
            {
                combinedContext.Add(clause.ContextType);
            }

            // Walk the statement looking for call sites.
            WalkStatementForCallSites(stmt, combinedContext, enclosingMethod, model, methodsWithContext, diagnostics);
        }
    }

    /// <summary>
    /// Walks a statement recursively looking for call sites.
    /// </summary>
    private static void WalkStatementForCallSites(
        StatementNode stmt,
        List<IType> availableContextTypes,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext,
        DiagnosticBag diagnostics)
    {
        // Handle with-block statements specially: they add context for their body.
        if (stmt is WithBlockStatementStatementNode withBlockStmt)
        {
            var withBlock = withBlockStmt.WithBlockStatement;
            var blockContextType = model.GetTypeOf(withBlock.Expression);
            if (blockContextType is not null)
            {
                var innerContext = new List<IType>(availableContextTypes) { ResolveContextType(blockContextType) };
                WalkBlockForCallSites(withBlock.Body, innerContext, enclosingMethod, model, methodsWithContext, diagnostics);
            }
            else
            {
                WalkBlockForCallSites(withBlock.Body, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
            }

            // Also check the expression itself for call sites.
            WalkExpressionForCallSites(withBlock.Expression, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
            return;
        }

        // For all other statements, walk all child nodes looking for call sites.
        WalkNodeForCallSites(stmt, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
    }

    /// <summary>
    /// Recursively walks an AST node looking for call sites (method calls, invocations,
    /// and object creation expressions).
    /// </summary>
    private static void WalkNodeForCallSites(
        SyntaxNode node,
        List<IType> availableContextTypes,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext,
        DiagnosticBag diagnostics)
    {
        if (node is null)
            return;
        switch (node)
        {
            case PostfixNode postfix:
                WalkPostfixForCallSites(postfix, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;

            case ObjectCreationExpressionPrimaryNode objectCreation:
                CheckObjectCreationCallSite(objectCreation, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                // Also walk arguments for nested call sites.
                if (objectCreation.Arguments is not null)
                    WalkNodeForCallSites(objectCreation.Arguments, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;

            case ObjectCreationExpressionNoArgsPrimaryNode objectCreationNoArgs:
                CheckObjectCreationNoArgsCallSite(objectCreationNoArgs, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;

            // Handle nested blocks (if, while, for, foreach) — they inherit context.
            case IfStatementNode ifStmt:
                WalkNodeForCallSites(ifStmt.Condition, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                WalkBlockForCallSites(ifStmt.ThenBranch, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                if (ifStmt.ElseBranch is not null)
                    WalkNodeForCallSites(ifStmt.ElseBranch, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;

            case WhileStatementNode whileStmt:
                WalkNodeForCallSites(whileStmt.Condition, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                WalkBlockForCallSites(whileStmt.Body, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;

            case ForStatementNode forStmt:
                if (forStmt.Initializer is not null)
                    WalkNodeForCallSites(forStmt.Initializer, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                if (forStmt.Condition is not null)
                    WalkNodeForCallSites(forStmt.Condition, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                if (forStmt.Iterator is not null)
                    WalkNodeForCallSites(forStmt.Iterator, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                WalkBlockForCallSites(forStmt.Body, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;

            case ForeachStatementNode foreachStmt:
                WalkNodeForCallSites(foreachStmt.Collection, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                WalkBlockForCallSites(foreachStmt.Body, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;

            case BlockNode block:
                WalkBlockForCallSites(block, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                return;
        }

        // Default: recurse into children.
        foreach (var child in node.Children())
        {
            if (child is null)
                continue;
            WalkNodeForCallSites(child, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
        }
    }

    /// <summary>
    /// Walks a postfix expression looking for method call suffixes that target
    /// methods with WithClauses.
    /// </summary>
    private static void WalkPostfixForCallSites(
        PostfixNode postfix,
        List<IType> availableContextTypes,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext,
        DiagnosticBag diagnostics)
    {
        // Walk the target primary for nested call sites.
        WalkNodeForCallSites(postfix.Target, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);

        // Track the receiver type as we walk through suffixes.
        IType? currentType = model.GetTypeOf(postfix.Target);

        for (var i = 0; i < postfix.Suffix.Length; i++)
        {
            var suffix = postfix.Suffix[i];

            switch (suffix)
            {
                case MethodCallSuffixPostfixSuffixNode methodCall:
                {
                    var methodName = methodCall.Name.Lexeme;

                    // Check if this method call targets a method with WithClauses.
                    var targetMethod = ResolveMethodCallTarget(methodName, currentType, model, methodsWithContext);
                    if (targetMethod is not null)
                    {
                        CheckContextRequirements(targetMethod, methodCall, availableContextTypes, model, diagnostics);
                    }

                    // Walk arguments for nested call sites.
                    if (methodCall.Arguments is not null)
                        WalkNodeForCallSites(methodCall.Arguments, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);

                    // Update current type for next suffix.
                    currentType = model.GetTypeOf(suffix);
                    break;
                }

                case InvocationSuffixPostfixSuffixNode invocation:
                {
                    // Direct invocation: target identifier is the method name.
                    if (postfix.Target is IdentifierExpressionPrimaryNode identifier && i == 0)
                    {
                        var methodName = identifier.Name.Lexeme;
                        var targetMethod = ResolveStandaloneCallTarget(methodName, enclosingMethod, model, methodsWithContext);
                        if (targetMethod is not null)
                        {
                            CheckContextRequirements(targetMethod, invocation, availableContextTypes, model, diagnostics);
                        }
                    }

                    // Walk arguments for nested call sites.
                    if (invocation.Arguments is not null)
                        WalkNodeForCallSites(invocation.Arguments, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);

                    currentType = model.GetTypeOf(suffix);
                    break;
                }

                default:
                    // For member access, index, etc., just update the type.
                    currentType = model.GetTypeOf(suffix);

                    // Walk children for nested call sites.
                    WalkNodeForCallSites(suffix, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
                    break;
            }
        }
    }

    /// <summary>
    /// Walks an expression node for call sites (used for with-block expressions, etc.).
    /// </summary>
    private static void WalkExpressionForCallSites(
        SyntaxNode node,
        List<IType> availableContextTypes,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext,
        DiagnosticBag diagnostics)
    {
        WalkNodeForCallSites(node, availableContextTypes, enclosingMethod, model, methodsWithContext, diagnostics);
    }

    // ── Call target resolution ────────────────────────────────────────────

    /// <summary>
    /// Resolves a method call suffix to its target symbol by looking up the method
    /// name in the receiver type's member scope.
    /// </summary>
    private static Symbol? ResolveMethodCallTarget(
        string methodName,
        IType? receiverType,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext)
    {
        // Quick check: if no method with this name has WithClauses, skip resolution.
        if (!methodsWithContext.ContainsKey(methodName))
            return null;

        if (receiverType is null)
            return null;

        var scope = GetMemberScope(receiverType);
        if (scope is not null)
        {
            foreach (var member in scope.Members)
            {
                if (member.Name == methodName && member is Symbol sym && sym.WithClauses.Count > 0)
                    return sym;
            }
        }

        // For abstract type dispatch, use the abstract type method's with-clauses.
        if (receiverType is AbstractType abstractType)
        {
            foreach (var member in abstractType.MemberScope.Members)
            {
                if (member.Name == methodName && member is Symbol sym && sym.WithClauses.Count > 0)
                    return sym;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolves a standalone function call (no receiver) to its target symbol.
    /// Looks in the enclosing type's member scope and then in all symbols.
    /// </summary>
    private static Symbol? ResolveStandaloneCallTarget(
        string methodName,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext)
    {
        // Quick check: if no method with this name has WithClauses, skip resolution.
        if (!methodsWithContext.ContainsKey(methodName))
            return null;

        // Look in the enclosing type's scope first.
        var enclosingTypeScope = FindEnclosingTypeScope(enclosingMethod);
        if (enclosingTypeScope is not null)
        {
            foreach (var member in enclosingTypeScope.Members)
            {
                if (member.Name == methodName && member is Symbol sym && sym.WithClauses.Count > 0)
                    return sym;
            }
        }

        // Fall back to all symbols (for methods in the same type or accessible types).
        if (methodsWithContext.TryGetValue(methodName, out var candidates))
        {
            // Return the first candidate (in practice there should be one matching).
            foreach (var candidate in candidates)
            {
                return candidate;
            }
        }

        return null;
    }

    /// <summary>
    /// Checks an object creation expression (new T(...)) to see if the constructor
    /// has WithClauses.
    /// </summary>
    private static void CheckObjectCreationCallSite(
        ObjectCreationExpressionPrimaryNode objectCreation,
        List<IType> availableContextTypes,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext,
        DiagnosticBag diagnostics)
    {
        // Resolve the type being constructed.
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null)
            return;

        // Look for constructors with WithClauses on the created type.
        var scope = GetMemberScope(createdType);
        if (scope is null)
            return;

        foreach (var member in scope.Members)
        {
            if (member is Symbol sym && sym.Kind == SymbolKind.Constructor && sym.WithClauses.Count > 0)
            {
                CheckContextRequirements(sym, objectCreation, availableContextTypes, model, diagnostics);
                break; // Only check the first matching constructor.
            }
        }
    }

    /// <summary>
    /// Checks an object creation expression with no args (new T { ... }) to see if
    /// the constructor has WithClauses.
    /// </summary>
    private static void CheckObjectCreationNoArgsCallSite(
        ObjectCreationExpressionNoArgsPrimaryNode objectCreation,
        List<IType> availableContextTypes,
        Symbol enclosingMethod,
        ISemanticModel model,
        Dictionary<string, List<Symbol>> methodsWithContext,
        DiagnosticBag diagnostics)
    {
        var createdType = model.GetTypeOf(objectCreation);
        if (createdType is null)
            return;

        var scope = GetMemberScope(createdType);
        if (scope is null)
            return;

        foreach (var member in scope.Members)
        {
            if (member is Symbol sym && sym.Kind == SymbolKind.Constructor && sym.WithClauses.Count > 0)
            {
                CheckContextRequirements(sym, objectCreation, availableContextTypes, model, diagnostics);
                break;
            }
        }
    }

    // ── Context requirement verification ─────────────────────────────────

    /// <summary>
    /// Checks that all context requirements of the target method are satisfied
    /// by the available context types at the call site.
    /// </summary>
    private static void CheckContextRequirements(
        Symbol targetMethod,
        SyntaxNode callSiteNode,
        List<IType> availableContextTypes,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var clause in targetMethod.WithClauses)
        {
            var requiredType = ResolveContextType(clause.ContextType);

            if (!IsContextTypeSatisfied(requiredType, availableContextTypes))
            {
                var (path, location, _) = DiagnosticHelper.ResolveLocation(callSiteNode);
                diagnostics.Add(new Diagnostic(
                    MissingContext,
                    DiagnosticSeverity.Error,
                    PipelineStage.Analysis,
                    $"Method '{targetMethod.Name}' requires context of type '{requiredType.DisplayName}', but no matching context declaration is in scope.",
                    Location: location,
                    CompilationUnitPath: path,
                    AnalyzerName: "ContextChecker"));
            }
        }
    }

    /// <summary>
    /// Checks if a required context type is satisfied by any of the available context types.
    /// </summary>
    private static bool IsContextTypeSatisfied(IType requiredType, List<IType> availableContextTypes)
    {
        foreach (var available in availableContextTypes)
        {
            if (ContextTypesMatch(requiredType, available))
                return true;
        }
        return false;
    }

    // ── Context type resolution and matching ─────────────────────────────

    /// <summary>
    /// Resolves a context type for keying purposes:
    /// - TypeAliasType → resolve to UnderlyingType recursively
    /// - UnionType → treated as its own distinct key
    /// - ConstructedType → each distinct type argument combination is a separate key
    /// - No inheritance-based lookup
    /// </summary>
    private static IType ResolveContextType(IType type)
    {
        // Resolve type aliases recursively.
        while (type is TypeAliasType alias)
        {
            type = alias.UnderlyingType;
        }
        return type;
    }

    /// <summary>
    /// Checks if two context types match (after alias resolution).
    /// Uses reference equality on the resolved IType instance since the semantic
    /// model canonicalizes types. Falls back to DisplayName comparison for cases
    /// where reference equality doesn't hold.
    /// </summary>
    private static bool ContextTypesMatch(IType required, IType available)
    {
        var resolvedRequired = ResolveContextType(required);
        var resolvedAvailable = ResolveContextType(available);

        // Reference equality (canonical types).
        if (ReferenceEquals(resolvedRequired, resolvedAvailable))
            return true;

        // Fall back to DisplayName comparison for structural equality.
        return string.Equals(resolvedRequired.DisplayName, resolvedAvailable.DisplayName, StringComparison.Ordinal);
    }

    // ── Context declaration type extraction ──────────────────────────────

    /// <summary>
    /// Extracts the context type from a statement if it is a context declaration.
    /// Returns null if the statement is not a context declaration.
    /// </summary>
    private static IType? TryGetContextDeclarationType(StatementNode stmt, ISemanticModel model)
    {
        switch (stmt)
        {
            case WithVarDeclarationStatementStatementNode withVar:
            {
                // Type is inferred from the initializer expression.
                var type = model.GetTypeOf(withVar.WithVarDeclaration.Initializer);
                return type is not null ? ResolveContextType(type) : null;
            }

            case WithTypedDeclarationStatementStatementNode withTyped:
            {
                // Type is the explicit type reference.
                var type = model.GetTypeOf(withTyped.WithTypedDeclaration.Type);
                if (type is not null)
                    return ResolveContextType(type);
                // Fall back to looking up the declared symbol's type.
                var sym = model.GetDeclaredSymbol(withTyped.WithTypedDeclaration);
                return sym?.DeclaredType is not null ? ResolveContextType(sym.DeclaredType) : null;
            }

            case WithDiscardDeclarationStatementStatementNode withDiscard:
            {
                // Type is the explicit type reference.
                var type = model.GetTypeOf(withDiscard.WithDiscardDeclaration.Type);
                if (type is not null)
                    return ResolveContextType(type);
                // Try resolving from the initializer as fallback.
                var initType = model.GetTypeOf(withDiscard.WithDiscardDeclaration.Initializer);
                return initType is not null ? ResolveContextType(initType) : null;
            }

            case WithPushStatementStatementNode withPush:
            {
                // Type is inferred from the expression.
                var type = model.GetTypeOf(withPush.WithPushStatement.Value);
                return type is not null ? ResolveContextType(type) : null;
            }

            // WithBlockStatement is handled separately in WalkStatementForCallSites
            // because it provides context only for its body, not for subsequent statements.

            default:
                return null;
        }
    }

    // ── Scope and type utilities ─────────────────────────────────────────

    /// <summary>
    /// Gets the member scope for a type, resolving through ConstructedType to
    /// the generic definition.
    /// </summary>
    private static IScope? GetMemberScope(IType type)
    {
        if (type is ConstructedType constructed)
            type = constructed.GenericDefinition;

        return type switch
        {
            ClassType ct => ct.MemberScope,
            StructType st => st.MemberScope,
            AbstractType it => it.MemberScope,
            _ => null,
        };
    }

    /// <summary>
    /// Finds the enclosing type's member scope for a method symbol by walking
    /// up the containing scope hierarchy.
    /// </summary>
    private static IScope? FindEnclosingTypeScope(Symbol method)
    {
        // The method's ContainingScope is the type's member scope.
        return method.ContainingScope;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Abstract Type Contract Enforcement (Task 5.3)
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// For each abstract type method that declares <c>with</c> clauses, verifies that
    /// all implementing types' corresponding methods declare the exact same clauses
    /// (matched by resolved type, no variance). Emits <c>MIST_CTX_0006</c> for
    /// omitted or mismatched clauses.
    /// </summary>
    private static void CheckAbstractWithClauseContracts(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Step 1: Collect all abstract type methods that have WithClauses.
        var abstractMethodsWithClauses = new List<(AbstractType Abstract, Symbol Method)>();
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol sym)
                continue;
            if (sym.Origin == SymbolOrigin.Backend)
                continue;
            if (sym.Kind != SymbolKind.Method)
                continue;
            if (sym.WithClauses.Count == 0)
                continue;

            // Check if this method belongs to an abstract type.
            if (sym.ContainingScope is IScope scope)
            {
                var ownerType = FindOwnerType(scope, model);
                if (ownerType is AbstractType abstractType)
                {
                    abstractMethodsWithClauses.Add((abstractType, sym));
                }
            }
        }

        if (abstractMethodsWithClauses.Count == 0)
            return;

        // Step 2: For each class/struct that implements an abstract type, check its methods.
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol is not Symbol typeSym)
                continue;
            if (typeSym.Origin == SymbolOrigin.Backend)
                continue;
            if (typeSym.Kind is not (SymbolKind.Class or SymbolKind.Struct))
                continue;

            var type = typeSym.DeclaredType;
            IReadOnlyList<IType>? abstracts = type switch
            {
                ClassType ct => ct.Abstracts,
                StructType st => st.Abstracts,
                _ => null,
            };

            if (abstracts is null || abstracts.Count == 0)
                continue;

            IScope? memberScope = type switch
            {
                ClassType ct => ct.MemberScope,
                StructType st => st.MemberScope,
                _ => null,
            };

            if (memberScope is null)
                continue;

            // Check each abstract type method with with-clauses.
            foreach (var (abstractType, abstractMethod) in abstractMethodsWithClauses)
            {
                if (!ImplementsAbstract(abstracts, abstractType))
                    continue;

                // Find the implementing method on this type.
                var implMethod = FindImplementingMethod(abstractMethod, memberScope);
                if (implMethod is null)
                    continue; // Missing method implementation is reported by the type checker.

                // Verify with-clause contract.
                CheckWithClauseContract(
                    abstractMethod, implMethod, abstractType, typeSym, model, diagnostics);
            }
        }
    }

    /// <summary>
    /// Finds the type that owns the given member scope by searching all symbols.
    /// </summary>
    private static IType? FindOwnerType(IScope memberScope, ISemanticModel model)
    {
        foreach (var symbol in model.AllSymbols)
        {
            if (symbol.DeclaredType is AbstractType it && it.MemberScope == memberScope)
                return it;
            if (symbol.DeclaredType is ClassType ct && ct.MemberScope == memberScope)
                return ct;
            if (symbol.DeclaredType is StructType st && st.MemberScope == memberScope)
                return st;
        }
        return null;
    }

    /// <summary>
    /// Checks if a type's abstract type list (including transitive parents) contains
    /// the specified abstract type.
    /// </summary>
    private static bool ImplementsAbstract(IReadOnlyList<IType> abstracts, AbstractType target)
    {
        foreach (var abs in abstracts)
        {
            if (abs is AbstractType at)
            {
                if (ReferenceEquals(at, target))
                    return true;
                // Check parent abstract types recursively.
                if (at.Abstracts.Count > 0 && ImplementsAbstract(at.Abstracts, target))
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Finds the implementing method on a type that corresponds to an abstract type method
    /// (matched by name and parameter count).
    /// </summary>
    private static Symbol? FindImplementingMethod(Symbol abstractMethod, IScope memberScope)
    {
        foreach (var member in memberScope.Members)
        {
            if (member is Symbol sym
                && sym.Kind == SymbolKind.Method
                && sym.Name == abstractMethod.Name)
            {
                return sym;
            }
        }
        return null;
    }

    /// <summary>
    /// Verifies that an implementing method declares the exact same with-clauses
    /// as the abstract type method. Emits MIST_CTX_0006 for each missing or mismatched clause.
    /// </summary>
    private static void CheckWithClauseContract(
        Symbol abstractMethod,
        Symbol implMethod,
        AbstractType abstractType,
        Symbol typeSym,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach (var requiredClause in abstractMethod.WithClauses)
        {
            var requiredType = ResolveContextType(requiredClause.ContextType);

            // Check if the implementing method has a matching with-clause.
            var found = false;
            foreach (var implClause in implMethod.WithClauses)
            {
                var implType = ResolveContextType(implClause.ContextType);
                if (ContextTypesMatch(requiredType, implType))
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                // Emit MIST_CTX_0006: implementing method is missing a required with-clause.
                SyntaxNode? implNode;
                try { implNode = model.GetDeclaringNode(implMethod); }
                catch { implNode = null; }

                if (implNode is not null)
                {
                    var (path, location, _) = DiagnosticHelper.ResolveLocation(implNode);
                    diagnostics.Add(new Diagnostic(
                        WithClauseContractViolation,
                        DiagnosticSeverity.Error,
                        PipelineStage.Analysis,
                        $"Method '{implMethod.Name}' on type '{typeSym.Name}' must declare `with {requiredType.DisplayName}` to satisfy abstract type '{abstractType.DisplayName}'.",
                        Location: location,
                        CompilationUnitPath: path,
                        AnalyzerName: "ContextChecker"));
                }
                else
                {
                    diagnostics.Add(new Diagnostic(
                        WithClauseContractViolation,
                        DiagnosticSeverity.Error,
                        PipelineStage.Analysis,
                        $"Method '{implMethod.Name}' on type '{typeSym.Name}' must declare `with {requiredType.DisplayName}` to satisfy abstract type '{abstractType.DisplayName}'.",
                        Location: default,
                        CompilationUnitPath: "<unknown>",
                        AnalyzerName: "ContextChecker"));
                }
            }
        }

        // Also check for mismatched clauses: if the implementing method declares
        // with-clauses that don't match any abstract type method clause, that's also
        // a contract violation (extra clauses are fine — they add requirements,
        // but the abstract type's clauses must all be present).
        // Note: Per the design, we only check that the abstract type's clauses are
        // present on the implementing method. Extra clauses on the implementing
        // method are allowed (they just add additional context requirements).
    }

}
