// Feature: first-class-functions — Unit tests for the LambdaExpression grammar
// and the lowest-precedence lambda model.
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 8.1, 9.1, 12.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Parsing;

/// <summary>
/// Unit tests verifying that the parser produces the expected lambda AST nodes
/// for every lambda syntax form: single implicit param, parenthesized params
/// (zero/one/many), typed and untyped params, expression and block bodies, and
/// the <c>static</c>/<c>async</c> modifiers.
///
/// Lambda is recognized at the lowest-precedence <c>Expression</c> level (a peer
/// of, but looser than, assignment). These tests assert that:
/// <list type="bullet">
/// <item>each lambda form surfaces as a <see cref="LambdaFormExpressionNode"/> at
/// the <c>Expression</c> level wrapping the correct <see cref="LambdaExpressionNode"/>
/// subtype;</item>
/// <item><c>f = x =&gt; x + 1</c> parses as <c>f = (x =&gt; x + 1)</c> (the lambda is
/// the assignment RHS) and <c>x =&gt; y =&gt; x + y</c> is right-associative;</item>
/// <item>a switch-arm <c>when</c> guard never swallows the arm's trailing
/// <c>=&gt;</c> into a lambda (the Requirement 12.5 regression).</item>
/// </list>
/// </summary>
public class LambdaExpressionParserTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Parse helpers ───────────────────────────────────────────────────────

    /// <summary>
    /// Parses a source string and asserts no parse errors occurred.
    /// Returns the <see cref="CompilationUnitNode"/> root.
    /// </summary>
    private static CompilationUnitNode ParseOrFail(string source)
    {
        var sourceText = new Mist.Common.Source.SourceText("test.mist", source);
        var bag = new DiagnosticBag();
        var tokens = Lexer.Lex(sourceText, bag);
        var result = Parser.Parse(tokens, bag);

        var errors = bag.ToImmutableArray()
            .Where(d => d.Severity == DiagnosticSeverity.Error)
            .ToList();
        Assert.True(errors.Count == 0,
            $"Unexpected parse errors:\n  {string.Join("\n  ", errors.Select(e => e.Message))}\nSource:\n{source}");

        return Assert.IsType<CompilationUnitNode>(result.Root);
    }

    /// <summary>
    /// Recursively walks the syntax tree (via <see cref="SyntaxNode.Children"/>)
    /// and returns the first node of type <typeparamref name="T"/>, or null.
    /// </summary>
    private static T? FindFirst<T>(SyntaxNode root) where T : SyntaxNode
    {
        if(root is T match)
        {
            return match;
        }

        foreach(var child in root.Children())
        {
            if(child is null)
            {
                continue;
            }

            var found = FindFirst<T>(child);
            if(found is not null)
            {
                return found;
            }
        }

        return null;
    }

    /// <summary>
    /// Parses <c>var f = {exprSource};</c> and returns the initializer
    /// <see cref="ExpressionNode"/> — the top-level expression of the RHS.
    /// </summary>
    private static ExpressionNode ParseVarInitializer(string exprSource)
    {
        var cu = ParseOrFail($"var f = {exprSource};");
        var item = Assert.IsType<StatementItemTopLevelItemNode>(cu.Items[0]);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(item.Statement);
        var decl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        return decl.Initializer;
    }

    /// <summary>
    /// Parses <c>{exprSource};</c> as a top-level expression statement and returns
    /// the contained <see cref="ExpressionNode"/> (the statement's root expression).
    /// </summary>
    private static ExpressionNode ParseExpressionStatement(string exprSource)
    {
        var cu = ParseOrFail($"{exprSource};");
        var item = Assert.IsType<StatementItemTopLevelItemNode>(cu.Items[0]);
        var stmt = Assert.IsType<ExpressionStatementStatementNode>(item.Statement);
        return stmt.ExpressionStatement.Expression;
    }

    /// <summary>
    /// Parses a lambda-bearing expression, asserts it surfaces as a
    /// <see cref="LambdaFormExpressionNode"/> at the <c>Expression</c> level, and
    /// returns the wrapped <see cref="LambdaExpressionNode"/>.
    /// </summary>
    private static LambdaExpressionNode ParseLambda(string lambdaExpr)
    {
        var expr = ParseVarInitializer(lambdaExpr);
        var form = Assert.IsType<LambdaFormExpressionNode>(expr);
        return form.Lambda;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Lambda forms — each surfaces as LambdaFormExpressionNode at Expression level
    // ═══════════════════════════════════════════════════════════════════════

    // ─── Single implicit param, expression body: x => x + 1 ─────────────────

    [Fact]
    public void SingleImplicitParam_ExpressionBody_ParsesAsExpressionLambda()
    {
        var lambda = ParseLambda("x => x + 1");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(exprLambda.Params);
        Assert.Equal("x", paramList.Name.Lexeme);
        Assert.Empty(exprLambda.Modifiers);
    }

    // ─── Zero params, expression body: () => 42 ─────────────────────────────

    [Fact]
    public void ZeroParams_ExpressionBody_ParsesAsParenthesizedWithNoParams()
    {
        var lambda = ParseLambda("() => 42");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(exprLambda.Params);
        Assert.Null(paramList.Params);
    }

    // ─── Single parenthesized untyped param: (x) => x + 1 ───────────────────

    [Fact]
    public void ParenthesizedSingleUntypedParam_ParsesAsUntypedLambdaParam()
    {
        var lambda = ParseLambda("(x) => x + 1");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(exprLambda.Params);
        Assert.NotNull(paramList.Params);

        var param = Assert.IsType<UntypedLambdaParamLambdaParamNode>(paramList.Params!.LambdaParam);
        Assert.Equal("x", param.Name.Lexeme);
        Assert.Empty(paramList.Params.LambdaParams);
    }

    // ─── Multiple untyped params: (x, y) => x + y ───────────────────────────

    [Fact]
    public void MultipleUntypedParams_ParsesAllParams()
    {
        var lambda = ParseLambda("(x, y) => x + y");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(exprLambda.Params);
        Assert.NotNull(paramList.Params);

        var first = Assert.IsType<UntypedLambdaParamLambdaParamNode>(paramList.Params!.LambdaParam);
        Assert.Equal("x", first.Name.Lexeme);

        _ = Assert.Single(paramList.Params.LambdaParams);
        var second = Assert.IsType<UntypedLambdaParamLambdaParamNode>(paramList.Params.LambdaParams[0]);
        Assert.Equal("y", second.Name.Lexeme);
    }

    // ─── Single typed param: (int x) => x + 1 ───────────────────────────────

    [Fact]
    public void TypedParam_ParsesAsTypedLambdaParam()
    {
        var lambda = ParseLambda("(int x) => x + 1");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(exprLambda.Params);
        Assert.NotNull(paramList.Params);

        var param = Assert.IsType<TypedLambdaParamLambdaParamNode>(paramList.Params!.LambdaParam);
        Assert.Equal("x", param.Name.Lexeme);
        var single = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(param.Type.First);
        _ = Assert.IsType<IntTypePrimitiveTypeNode>(single.PrimitiveType);
    }

    // ─── Multiple typed params: (int x, string y) => x ──────────────────────

    [Fact]
    public void MultipleTypedParams_ParsesAllTypedParams()
    {
        var lambda = ParseLambda("(int x, string y) => x");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(exprLambda.Params);
        Assert.NotNull(paramList.Params);

        var first = Assert.IsType<TypedLambdaParamLambdaParamNode>(paramList.Params!.LambdaParam);
        Assert.Equal("x", first.Name.Lexeme);
        var firstType = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(first.Type.First);
        _ = Assert.IsType<IntTypePrimitiveTypeNode>(firstType.PrimitiveType);

        _ = Assert.Single(paramList.Params.LambdaParams);
        var second = Assert.IsType<TypedLambdaParamLambdaParamNode>(paramList.Params.LambdaParams[0]);
        Assert.Equal("y", second.Name.Lexeme);
        var secondType = Assert.IsType<PrimitiveSingleTypeRefSingleTypeRefNode>(second.Type.First);
        _ = Assert.IsType<StringTypePrimitiveTypeNode>(secondType.PrimitiveType);
    }

    // ─── Block body, single implicit param: x => { return x + 1; } ──────────

    [Fact]
    public void SingleImplicitParam_BlockBody_ParsesAsBlockLambda()
    {
        var lambda = ParseLambda("x => { return x + 1; }");

        var blockLambda = Assert.IsType<BlockLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(blockLambda.Params);
        Assert.Equal("x", paramList.Name.Lexeme);
        Assert.NotEmpty(blockLambda.Body.Statements);
    }

    // ─── Block body, parenthesized params: (x, y) => { return x + y; } ──────

    [Fact]
    public void ParenthesizedParams_BlockBody_ParsesAsBlockLambda()
    {
        var lambda = ParseLambda("(x, y) => { return x + y; }");

        var blockLambda = Assert.IsType<BlockLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(blockLambda.Params);
        Assert.NotNull(paramList.Params);
        Assert.NotEmpty(blockLambda.Body.Statements);
    }

    // ─── static modifier: static x => x ─────────────────────────────────────

    [Fact]
    public void StaticModifier_ParsesAsStaticLambdaModifier()
    {
        var lambda = ParseLambda("static x => x");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        _ = Assert.Single(exprLambda.Modifiers);
        _ = Assert.IsType<StaticLambdaModifierLambdaModifierNode>(exprLambda.Modifiers[0]);
    }

    // ─── async modifier: async () => 42 ─────────────────────────────────────

    [Fact]
    public void AsyncModifier_ParsesAsAsyncLambdaModifier()
    {
        var lambda = ParseLambda("async () => 42");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        _ = Assert.Single(exprLambda.Modifiers);
        _ = Assert.IsType<AsyncLambdaModifierLambdaModifierNode>(exprLambda.Modifiers[0]);
    }

    // ─── static async combination: static async () => 42 ───────────────────

    [Fact]
    public void StaticAsyncModifiers_ParseInOrder()
    {
        var lambda = ParseLambda("static async () => 42");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        Assert.Equal(2, exprLambda.Modifiers.Length);
        _ = Assert.IsType<StaticLambdaModifierLambdaModifierNode>(exprLambda.Modifiers[0]);
        _ = Assert.IsType<AsyncLambdaModifierLambdaModifierNode>(exprLambda.Modifiers[1]);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Precedence model: lambda is the lowest-precedence expression form
    // ═══════════════════════════════════════════════════════════════════════

    // ─── f = x => x + 1   parses as   f = (x => x + 1) ──────────────────────

    [Fact]
    public void AssignmentRhs_IsTheLambda_NotTheOtherWayAround()
    {
        // The whole statement is an assignment whose RHS is the lambda; lambda
        // binds looser than `=`, so the parse is `f = (x => x + 1)`, never
        // `(f = x) => x + 1`.
        var expr = ParseExpressionStatement("f = x => x + 1");

        var assignmentForm = Assert.IsType<AssignmentFormExpressionNode>(expr);
        var assignment = assignmentForm.Assignment;

        // It is genuinely an assignment (has the `=` operator).
        _ = Assert.NotNull(assignment.Operator);
        Assert.Equal("=", assignment.Operator!.Value.Lexeme);

        // The assignment target is the identifier `f`.
        var target = FindFirst<IdentifierExpressionPrimaryNode>(assignment.Target);
        Assert.NotNull(target);
        Assert.Equal("f", target!.Name.Lexeme);

        // The RHS value is the lambda itself.
        var rhsLambdaForm = Assert.IsType<LambdaFormExpressionNode>(assignment.Value);
        var rhsLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(rhsLambdaForm.Lambda);
        var rhsParams = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(rhsLambda.Params);
        Assert.Equal("x", rhsParams.Name.Lexeme);
    }

    // ─── x => y => x + y   parses right-associatively as x => (y => (x + y)) ─

    [Fact]
    public void NestedLambda_IsRightAssociative()
    {
        var expr = ParseExpressionStatement("x => y => x + y");

        // Outer lambda: x => <body>
        var outerForm = Assert.IsType<LambdaFormExpressionNode>(expr);
        var outer = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(outerForm.Lambda);
        var outerParam = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(outer.Params);
        Assert.Equal("x", outerParam.Name.Lexeme);

        // The outer body is itself a lambda (right-associative nesting).
        var innerForm = Assert.IsType<LambdaFormExpressionNode>(outer.Body);
        var inner = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(innerForm.Lambda);
        var innerParam = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(inner.Params);
        Assert.Equal("y", innerParam.Name.Lexeme);

        // The innermost body `x + y` is NOT a lambda.
        Assert.IsNotType<LambdaFormExpressionNode>(inner.Body);
        Assert.Null(FindFirst<LambdaExpressionNode>(inner.Body));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Regression — switch-arm guard never captures the arm's trailing `=>`
    // **Validates: Requirements 12.5**
    // ═══════════════════════════════════════════════════════════════════════

    private static SwitchArmListNode ParseSwitchArms(string switchExpr)
    {
        var cu = ParseOrFail($"var r = {switchExpr};");
        var suffix = FindFirst<SwitchExpressionSuffixPostfixSuffixNode>(cu);
        Assert.NotNull(suffix);
        Assert.NotNull(suffix!.Arms);
        return suffix.Arms!;
    }

    // ─── int n when predicate => n.ToString()  (the core regression) ────────

    [Fact]
    public void SwitchArm_IdentifierGuard_DoesNotCaptureTrailingLambda()
    {
        var arms = ParseSwitchArms("value switch { int n when predicate => n.ToString(), _ => \"none\" }");

        // Exactly two well-formed arms.
        _ = Assert.Single(arms.SwitchArms); // first arm + 1 more = 2 total
        var firstArm = arms.SwitchArm;
        var secondArm = arms.SwitchArms[0];

        // First arm: `int n` type pattern with a `when predicate` guard.
        var typePattern = FindFirst<TypePatternPrimaryPatternNode>(firstArm.Pattern);
        Assert.NotNull(typePattern);
        Assert.Equal("n", typePattern!.Name.Lexeme);

        Assert.NotNull(firstArm.Guard);

        // The guard condition is the bare identifier `predicate` — NOT a lambda.
        // (Condition is a VoidCoalescingNode, which structurally sits below the
        // lambda alternative, so the `=>` is left for the arm.)
        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Guard!.Condition));
        var guardIdent = FindFirst<IdentifierExpressionPrimaryNode>(firstArm.Guard.Condition);
        Assert.NotNull(guardIdent);
        Assert.Equal("predicate", guardIdent!.Name.Lexeme);

        // The first arm's result is `n.ToString()` — the `=>` belongs to the arm.
        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Result));
        var resultCall = FindFirst<MethodCallSuffixPostfixSuffixNode>(firstArm.Result);
        Assert.NotNull(resultCall);
        Assert.Equal("ToString", resultCall!.Name.Lexeme);
        var resultTarget = FindFirst<IdentifierExpressionPrimaryNode>(firstArm.Result);
        Assert.NotNull(resultTarget);
        Assert.Equal("n", resultTarget!.Name.Lexeme);

        // Second arm: discard pattern, no guard, string-literal result.
        Assert.NotNull(FindFirst<DiscardPatternPrimaryPatternNode>(secondArm.Pattern));
        Assert.Null(secondArm.Guard);
        Assert.NotNull(FindFirst<StringLiteralExpressionPrimaryNode>(secondArm.Result));
    }

    // ─── int n when n > 7 => n.ToString()  (relational guard, above the floor) ─

    [Fact]
    public void SwitchArm_RelationalGuard_ParsesGuardAndResultCorrectly()
    {
        var arms = ParseSwitchArms("value switch { int n when n > 7 => n.ToString(), _ => \"none\" }");

        _ = Assert.Single(arms.SwitchArms);
        var firstArm = arms.SwitchArm;

        Assert.NotNull(firstArm.Guard);
        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Guard!.Condition));

        // Guard is the comparison `n > 7`.
        var comparison = FindFirst<RelationalComparisonRelationalNode>(firstArm.Guard.Condition);
        Assert.NotNull(comparison);
        Assert.Equal(">", comparison!.Operator.Lexeme);

        // Result is `n.ToString()`.
        var resultCall = FindFirst<MethodCallSuffixPostfixSuffixNode>(firstArm.Result);
        Assert.NotNull(resultCall);
        Assert.Equal("ToString", resultCall!.Name.Lexeme);
    }

    // ─── int n when isOdd(n) => n.ToString()  (invocation guard) ────────────

    [Fact]
    public void SwitchArm_InvocationGuard_ParsesGuardAndResultCorrectly()
    {
        var arms = ParseSwitchArms("value switch { int n when isOdd(n) => n.ToString(), _ => \"none\" }");

        _ = Assert.Single(arms.SwitchArms);
        var firstArm = arms.SwitchArm;

        Assert.NotNull(firstArm.Guard);
        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Guard!.Condition));

        // Guard is the invocation `isOdd(n)`.
        Assert.NotNull(FindFirst<InvocationSuffixPostfixSuffixNode>(firstArm.Guard.Condition));
        var guardTarget = FindFirst<IdentifierExpressionPrimaryNode>(firstArm.Guard.Condition);
        Assert.NotNull(guardTarget);
        Assert.Equal("isOdd", guardTarget!.Name.Lexeme);

        // Result is `n.ToString()` — the `=>` is the arm separator.
        var resultCall = FindFirst<MethodCallSuffixPostfixSuffixNode>(firstArm.Result);
        Assert.NotNull(resultCall);
        Assert.Equal("ToString", resultCall!.Name.Lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Regression — non-lambda value positions must NOT parse as lambdas
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void ParenthesizedExpression_IsNotParsedAsLambda()
    {
        var cu = ParseOrFail("var x = (1 + 2);");

        Assert.Null(FindFirst<LambdaExpressionNode>(cu));
        Assert.NotNull(FindFirst<ParenthesizedExpressionPrimaryNode>(cu));
    }

    [Fact]
    public void BareIdentifier_IsNotParsedAsLambda()
    {
        var cu = ParseOrFail("var x = y;");

        Assert.Null(FindFirst<LambdaExpressionNode>(cu));
        Assert.NotNull(FindFirst<IdentifierExpressionPrimaryNode>(cu));
    }
}