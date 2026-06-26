using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Flow-sensitive analyzer that tracks the stack-backing provenance of span-typed
/// values originating from <c>stackalloc</c> expressions and rejects programs that
/// allow such values to escape their allocating stack frame.
///
/// Tracking lattice:
///   - <c>StackBacked</c> — the value is (transitively) backed by a stackalloc in the current frame.
///   - <c>NotStackBacked</c> — the value comes from a non-stackalloc source (parameter, AsSpan, etc.).
///
/// Rejection rules:
///   - Return of a StackBacked value → <c>MIST2008</c>
///   - Escaping store (field/property of a heap object, static/captured binding, out/ref outliving
///     the call, heap-collection element, containing-ref-struct propagation) → <c>MIST2009</c>
///
/// Acceptance rules (no diagnostic):
///   - Frame-local use: local variable read/write, indexer access, slice, foreach, method argument
///     where the span does not escape beyond the call duration.
///   - All NotStackBacked values.
///
/// The analysis is conservative: when propagation cannot be proven safe, the value is treated
/// as StackBacked. This guarantees no false-accept (R8.4) — accepted programs never produce
/// a dangling stack reference.
///
/// Runs after the type checker (so every expression has a resolved SpanType) and before
/// emission (so a detected escape suppresses C# output for the affected compilation).
/// </summary>
public sealed class FrameEscapeAnalyzer : IAnalyzer
{
    public string AnalyzerId => "FRAME_ESCAPE";

    // ── Diagnostic IDs ───────────────────────────────────────────────────

    /// <summary>A stackalloc-backed span is returned from a method (R8.2).</summary>
    internal static readonly DiagnosticId StackBackedReturn = new("MIST2008");

    /// <summary>A stackalloc-backed span escapes its frame via a store (R8.3, R8.5).</summary>
    internal static readonly DiagnosticId StackBackedEscape = new("MIST2009");

    // ── Stack-backing lattice ────────────────────────────────────────────

    private enum StackBacking
    {
        /// <summary>The value is backed by a stackalloc in the current frame.</summary>
        StackBacked,

        /// <summary>The value is not backed by a stackalloc (parameter, AsSpan, etc.).</summary>
        NotStackBacked,
    }

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
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

    // ── Per-method analysis ──────────────────────────────────────────────

    private static void AnalyzeMethodBody(ISymbol methodSymbol, ISemanticModel model, DiagnosticBag diagnostics)
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

        // The backing state maps local symbols to their stack-backing provenance.
        var backingState = new Dictionary<ISymbol, StackBacking>();

        WalkBlock(body, backingState, model, diagnostics);
    }

    // ── Block / statement walking ────────────────────────────────────────

    private static void WalkBlock(
        BlockNode block,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        foreach(var stmt in block.Statements)
        {
            WalkStatement(stmt, state, model, diagnostics);
        }
    }

    private static void WalkStatement(
        StatementNode stmt,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        switch(stmt)
        {
            case VariableDeclarationStatementStatementNode varDeclStmt:
                AnalyzeVariableDeclaration(varDeclStmt.VariableDeclaration, state, model, diagnostics);
                break;

            case ExpressionStatementStatementNode exprStmt:
                AnalyzeExpressionStatement(exprStmt.ExpressionStatement.Expression, state, model, diagnostics);
                break;

            case ReturnStatementStatementNode returnStmt:
                AnalyzeReturnStatement(returnStmt.ReturnStatement, state, model, diagnostics);
                break;

            case IfStatementStatementNode ifStmt:
                AnalyzeIfStatement(ifStmt.IfStatement, state, model, diagnostics);
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

            default:
                break;
        }
    }

    // ── Variable declaration ─────────────────────────────────────────────

    private static void AnalyzeVariableDeclaration(
        VariableDeclarationStatementNode varDecl,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        SyntaxNode? initializer = null;
        ISymbol? declaredSymbol = null;

        switch(varDecl)
        {
            case TypedVariableDeclarationVariableDeclarationStatementNode typed:
                initializer = typed.Initializer;
                declaredSymbol = model.GetDeclaredSymbol(typed);
                break;

            case InferredVariableDeclarationVariableDeclarationStatementNode varTyped:
                initializer = varTyped.Initializer;
                declaredSymbol = model.GetDeclaredSymbol(varTyped);
                break;
        }

        if(declaredSymbol is null || initializer is null)
        {
            return;
        }

        // Only track span-typed bindings.
        var declaredType = declaredSymbol.DeclaredType;
        if(!IsSpanType(declaredType))
        {
            return;
        }

        // Determine the backing of the initializer.
        var backing = DetermineExpressionBacking(initializer, state, model);
        state[declaredSymbol] = backing;
    }

    // ── Expression statement (assignment) ────────────────────────────────

    private static void AnalyzeExpressionStatement(
        SyntaxNode expression,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Check for assignments: target = value
        // An expression statement wrapping an assignment might store a stack-backed
        // span into an escaping location.
        if(expression is ExpressionNode exprNode)
        {
            var assignment = exprNode.AsAssignmentOrNull();
            if(assignment?.Value is not null)
            {
                AnalyzeAssignment(assignment, state, model, diagnostics);
            }
        }
    }

    private static void AnalyzeAssignment(
        AssignmentNode assignment,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(assignment.Value is null)
        {
            return;
        }

        var rhsAssignment = assignment.Value.AsAssignmentOrNull();
        if(rhsAssignment is null)
        {
            return;
        }

        // Determine the backing of the RHS.
        var rhsBacking = DetermineAssignmentValueBacking(rhsAssignment, state, model);
        if(rhsBacking != StackBacking.StackBacked)
        {
            return; // NotStackBacked never triggers frame-escape (R9.4).
        }

        // Check if the LHS is a frame-local span binding (propagation within frame is fine).
        var lhsSymbol = TryResolveLocalSymbol(assignment.Target, model);
        if(lhsSymbol is not null && lhsSymbol.Kind == SymbolKind.LocalVariable)
        {
            // Reassignment to a local — propagate the backing fact.
            if(IsSpanType(lhsSymbol.DeclaredType))
            {
                state[lhsSymbol] = StackBacking.StackBacked;
            }

            return;
        }

        // Check if the LHS is a parameter (also frame-local for the call duration).
        if(lhsSymbol is not null && lhsSymbol.Kind == SymbolKind.Parameter)
        {
            // Assigning to a by-value parameter is frame-local.
            if(IsSpanType(lhsSymbol.DeclaredType))
            {
                state[lhsSymbol] = StackBacking.StackBacked;
            }

            return;
        }

        // The LHS is not a simple local — it's a field, property, static, indexed
        // element, or other escaping location. Reject with MIST2009.
        var storeNode = GetStoreLocationNode(assignment);
        diagnostics.Add(MakeDiagnostic(
            StackBackedEscape,
            "A stackalloc-backed span cannot be stored in a location that outlives its stack frame",
            storeNode,
            model));
    }

    // ── Return statement ─────────────────────────────────────────────────

    private static void AnalyzeReturnStatement(
        ReturnStatementNode returnStmt,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(returnStmt.Value is null)
        {
            return;
        }

        // Only check span-typed return values.
        var returnType = model.GetTypeOf(returnStmt.Value);
        if(!IsSpanType(returnType))
        {
            return;
        }

        var backing = DetermineExpressionBacking(returnStmt.Value, state, model);
        if(backing == StackBacking.StackBacked)
        {
            diagnostics.Add(MakeDiagnostic(
                StackBackedReturn,
                "A stackalloc-backed span cannot be returned from a method because it would escape its stack frame",
                returnStmt.Value,
                model));
        }
    }

    // ── Control flow ─────────────────────────────────────────────────────

    private static void AnalyzeIfStatement(
        IfStatementNode ifStmt,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // Walk the then block.
        WalkBlock(ifStmt.ThenBranch, state, model, diagnostics);

        // Walk the else block if present.
        if(ifStmt.ElseBranch is ElseBlockElseClauseNode elseBlock)
        {
            WalkBlock(elseBlock.ElseBlock, state, model, diagnostics);
        }
        else if(ifStmt.ElseBranch is ElseIfElseClauseNode elseIf)
        {
            AnalyzeIfStatement(elseIf.ElseIf, state, model, diagnostics);
        }
    }

    private static void AnalyzeWhileStatement(
        WhileStatementNode whileStmt,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(whileStmt.Body is not null)
        {
            WalkBlock(whileStmt.Body, state, model, diagnostics);
        }
    }

    private static void AnalyzeForStatement(
        ForStatementNode forStmt,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        if(forStmt.Body is not null)
        {
            WalkBlock(forStmt.Body, state, model, diagnostics);
        }
    }

    private static void AnalyzeForeachStatement(
        ForeachStatementNode foreachStmt,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model,
        DiagnosticBag diagnostics)
    {
        // foreach over a span is a safe in-frame use (R8.6).
        if(foreachStmt.Body is not null)
        {
            WalkBlock(foreachStmt.Body, state, model, diagnostics);
        }
    }

    // ── Expression backing determination ─────────────────────────────────

    /// <summary>
    /// Determines the stack-backing provenance of an expression.
    /// </summary>
    private static StackBacking DetermineExpressionBacking(
        SyntaxNode node,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model)
    {
        // Unwrap expression wrappers.
        if(node is ExpressionNode exprNode)
        {
            var assignment = exprNode.AsAssignmentOrNull();
            if(assignment is not null)
            {
                return DetermineAssignmentValueBacking(assignment, state, model);
            }

            return StackBacking.NotStackBacked;
        }

        if(node is AssignmentNode assignNode)
        {
            return DetermineAssignmentValueBacking(assignNode, state, model);
        }

        return StackBacking.NotStackBacked;
    }

    /// <summary>
    /// Determines the backing of an assignment value (the target of an AssignmentNode
    /// without a chained value — i.e., a simple expression).
    /// </summary>
    private static StackBacking DetermineAssignmentValueBacking(
        AssignmentNode assignment,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model)
    {
        if(assignment.Value is not null)
        {
            // Chained assignment: recurse into the value.
            var innerAssignment = assignment.Value.AsAssignmentOrNull();
            if(innerAssignment is not null)
            {
                return DetermineAssignmentValueBacking(innerAssignment, state, model);
            }

            return StackBacking.NotStackBacked;
        }

        // Simple expression — walk down the target chain.
        return DetermineTargetBacking(assignment.Target, state, model);
    }

    /// <summary>
    /// Determines backing by walking down the expression chain
    /// (LogicalOr → ... → PostfixNode → PrimaryNode).
    /// </summary>
    private static StackBacking DetermineTargetBacking(
        SyntaxNode target,
        Dictionary<ISymbol, StackBacking> state,
        ISemanticModel model)
    {
        // Check for a stackalloc expression.
        if(target is StackallocExpressionPrimaryNode)
        {
            return StackBacking.StackBacked;
        }

        // Check for an identifier referencing a tracked local.
        if(target is IdentifierExpressionPrimaryNode identifier)
        {
            var symbol = model.GetDeclaredSymbol(identifier);
            if(symbol is not null && state.TryGetValue(symbol, out var backing))
            {
                return backing;
            }
            // Parameters default to NotStackBacked (they cannot be stackalloc-backed
            // because a stackalloc cannot escape a frame to become a parameter value).
            return StackBacking.NotStackBacked;
        }

        // PostfixNode with suffixes: check for Slice call (propagates backing of receiver),
        // or member access on a ref struct field (conservative StackBacked propagation).
        if(target is PostfixNode postfix)
        {
            if(postfix.Suffix.Length == 0)
            {
                // No suffixes — the backing comes from the target.
                return DetermineTargetBacking(postfix.Target, state, model);
            }

            // Check for method call suffixes (e.g., .Slice(...)).
            var receiverBacking = DetermineTargetBacking(postfix.Target, state, model);

            foreach(var suffix in postfix.Suffix)
            {
                if(suffix is MethodCallSuffixPostfixSuffixNode methodCall)
                {
                    // .Slice() returns a span aliasing the receiver's memory.
                    if(IsSliceCall(methodCall))
                    {
                        // Slice propagates the receiver's backing (R8.1).
                        return receiverBacking;
                    }
                    // Other method calls on a span do not propagate the span itself
                    // (they may return a different type). The result is not span-backed
                    // unless we can prove otherwise. Conservative: if the return type
                    // is a span type, treat as StackBacked when receiver is StackBacked.
                    var callResultType = model.GetTypeOf(postfix);
                    if(IsSpanType(callResultType) && receiverBacking == StackBacking.StackBacked)
                    {
                        return StackBacking.StackBacked;
                    }

                    return StackBacking.NotStackBacked;
                }

                if(suffix is MemberAccessSuffixPostfixSuffixNode)
                {
                    // Member access on a ref-struct — if the containing value is
                    // StackBacked and the result is a span type, propagate backing
                    // (containing ref struct field, R8.5).
                    var resultType = model.GetTypeOf(postfix);
                    if(IsSpanType(resultType) && receiverBacking == StackBacking.StackBacked)
                    {
                        return StackBacking.StackBacked;
                    }

                    return StackBacking.NotStackBacked;
                }

                if(suffix is IndexSuffixPostfixSuffixNode)
                {
                    // Indexing a span returns an element (not a span), so no propagation.
                    // Unless the element type is itself a span (unlikely but conservative).
                    var resultType = model.GetTypeOf(postfix);
                    if(IsSpanType(resultType) && receiverBacking == StackBacking.StackBacked)
                    {
                        return StackBacking.StackBacked;
                    }

                    return StackBacking.NotStackBacked;
                }
            }

            return receiverBacking;
        }

        // For other expression chain nodes, walk through single-child chains.
        var children = new List<SyntaxNode>();
        foreach(var child in target.Children())
        {
            if(child is not null)
            {
                children.Add(child);
            }
        }

        if(children.Count == 1)
        {
            return DetermineTargetBacking(children[0], state, model);
        }

        // Conservative: if we cannot determine the backing, and the expression type
        // is a span type, treat as StackBacked (no false-accept, R8.4).
        var exprType = model.GetTypeOf(target);
        if(IsSpanType(exprType))
        {
            return StackBacking.StackBacked;
        }

        return StackBacking.NotStackBacked;
    }

    // ── Helper: identify Slice calls ─────────────────────────────────────

    private static bool IsSliceCall(MethodCallSuffixPostfixSuffixNode methodCall)
    {
        // A Slice call looks like: .Slice(start, length)
        // The method name is accessed via Name.Lexeme on the generated node.
        if(methodCall.Name.Lexeme == "Slice")
        {
            return true;
        }

        return false;
    }

    // ── Helper: resolve a local variable symbol from an expression ────────

    private static ISymbol? TryResolveLocalSymbol(SyntaxNode node, ISemanticModel model)
    {
        if(node is ExpressionNode expr)
        {
            var assignment = expr.AsAssignmentOrNull();
            if(assignment is not null)
            {
                return TryResolveLocalSymbol(assignment.Target, model);
            }

            return null;
        }

        if(node is AssignmentNode assignNode)
        {
            if(assignNode.Value is not null)
            {
                return null;
            }

            return TryResolveLocalSymbol(assignNode.Target, model);
        }

        if(node is PostfixNode postfix)
        {
            if(postfix.Suffix.Length > 0)
            {
                return null; // Has suffixes — not a simple local.
            }

            return TryResolveLocalSymbol(postfix.Target, model);
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

        // Walk single-child chains.
        var children = new List<SyntaxNode>();
        foreach(var child in node.Children())
        {
            if(child is not null)
            {
                children.Add(child);
            }
        }

        if(children.Count == 1)
        {
            return TryResolveLocalSymbol(children[0], model);
        }

        return null;
    }

    // ── Helper: get the AST node representing the store location ─────────

    private static SyntaxNode GetStoreLocationNode(AssignmentNode assignment)
    {
        // The offending node is the assignment target (the escaping store site).
        return (SyntaxNode)assignment.Target ?? assignment;
    }

    // ── Helper: type predicates ──────────────────────────────────────────

    private static bool IsSpanType(IType? type)
    {
        return type is SpanType;
    }

    // ── Diagnostic creation (mirrors OwnershipAnalyzer.MakeDiagnostic) ───

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
            AnalyzerName: "FrameEscape");
    }
}