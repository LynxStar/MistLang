// Feature: first-class-functions
// **Validates: Requirements 4.1, 4.2, 4.3, 4.4, 4.5, 4.6, 5.1, 5.2, 8.1, 9.1, 12.5**

using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.Lexing.Contracts;
using Mist.Parsing.Contracts;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.Tests.Parsing;

public class LambdaExpressionParserTests
{
    private static readonly ILexer Lexer = new Mist.Lexing.MistLexer();
    private static readonly IParser Parser = new Mist.Parsing.MistParser();

    // ─── Parse helpers ───────────────────────────────────────────────────────

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

    private static ExpressionNode ParseVarInitializer(string exprSource)
    {
        var cu = ParseOrFail($"var f = {exprSource};");
        var item = Assert.IsType<StatementItemTopLevelItemNode>(cu.Items[0]);
        var stmt = Assert.IsType<VariableDeclarationStatementStatementNode>(item.Statement);
        var decl = Assert.IsType<InferredVariableDeclarationVariableDeclarationStatementNode>(stmt.VariableDeclaration);
        return decl.Initializer;
    }

    private static ExpressionNode ParseExpressionStatement(string exprSource)
    {
        var cu = ParseOrFail($"{exprSource};");
        var item = Assert.IsType<StatementItemTopLevelItemNode>(cu.Items[0]);
        var stmt = Assert.IsType<ExpressionStatementStatementNode>(item.Statement);
        return stmt.ExpressionStatement.Expression;
    }

    private static LambdaExpressionNode ParseLambda(string lambdaExpr)
    {
        var expr = ParseVarInitializer(lambdaExpr);
        var form = Assert.IsType<LambdaFormExpressionNode>(expr);
        return form.Lambda;
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Lambda forms — each surfaces as LambdaFormExpressionNode at Expression level
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void SingleImplicitParam_ExpressionBody_ParsesAsExpressionLambda()
    {
        var lambda = ParseLambda("x => x + 1");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(exprLambda.Params);
        Assert.Equal("x", paramList.Name.Lexeme);
        Assert.Empty(exprLambda.Modifiers);
    }

    [Fact]
    public void ZeroParams_ExpressionBody_ParsesAsParenthesizedWithNoParams()
    {
        var lambda = ParseLambda("() => 42");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(exprLambda.Params);
        Assert.Null(paramList.Params);
    }

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

    [Fact]
    public void SingleImplicitParam_BlockBody_ParsesAsBlockLambda()
    {
        var lambda = ParseLambda("x => { return x + 1; }");

        var blockLambda = Assert.IsType<BlockLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(blockLambda.Params);
        Assert.Equal("x", paramList.Name.Lexeme);
        Assert.NotEmpty(blockLambda.Body.Statements);
    }

    [Fact]
    public void ParenthesizedParams_BlockBody_ParsesAsBlockLambda()
    {
        var lambda = ParseLambda("(x, y) => { return x + y; }");

        var blockLambda = Assert.IsType<BlockLambdaLambdaExpressionNode>(lambda);
        var paramList = Assert.IsType<ParenthesizedLambdaParamsLambdaParameterListNode>(blockLambda.Params);
        Assert.NotNull(paramList.Params);
        Assert.NotEmpty(blockLambda.Body.Statements);
    }

    [Fact]
    public void StaticModifier_ParsesAsStaticLambdaModifier()
    {
        var lambda = ParseLambda("static x => x");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        _ = Assert.Single(exprLambda.Modifiers);
        _ = Assert.IsType<StaticLambdaModifierLambdaModifierNode>(exprLambda.Modifiers[0]);
    }

    [Fact]
    public void AsyncModifier_ParsesAsAsyncLambdaModifier()
    {
        var lambda = ParseLambda("async () => 42");

        var exprLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(lambda);
        _ = Assert.Single(exprLambda.Modifiers);
        _ = Assert.IsType<AsyncLambdaModifierLambdaModifierNode>(exprLambda.Modifiers[0]);
    }

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

    [Fact]
    public void AssignmentRhs_IsTheLambda_NotTheOtherWayAround()
    {
        // Lambda binds looser than `=`, so the parse is `f = (x => x + 1)`, never `(f = x) => x + 1`.
        var expr = ParseExpressionStatement("f = x => x + 1");

        var assignmentForm = Assert.IsType<AssignmentFormExpressionNode>(expr);
        var assignment = assignmentForm.Assignment;

        _ = Assert.NotNull(assignment.Group0?.Operator);
        Assert.Equal("=", (assignment.Group0?.Operator)!.Value.Lexeme);

        var target = FindFirst<IdentifierExpressionPrimaryNode>(assignment.Target);
        Assert.NotNull(target);
        Assert.Equal("f", target!.Name.Lexeme);

        var rhsLambdaForm = Assert.IsType<LambdaFormExpressionNode>(assignment.Group0?.Value);
        var rhsLambda = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(rhsLambdaForm.Lambda);
        var rhsParams = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(rhsLambda.Params);
        Assert.Equal("x", rhsParams.Name.Lexeme);
    }

    [Fact]
    public void NestedLambda_IsRightAssociative()
    {
        var expr = ParseExpressionStatement("x => y => x + y");

        var outerForm = Assert.IsType<LambdaFormExpressionNode>(expr);
        var outer = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(outerForm.Lambda);
        var outerParam = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(outer.Params);
        Assert.Equal("x", outerParam.Name.Lexeme);

        var innerForm = Assert.IsType<LambdaFormExpressionNode>(outer.Body);
        var inner = Assert.IsType<ExpressionLambdaLambdaExpressionNode>(innerForm.Lambda);
        var innerParam = Assert.IsType<SingleImplicitParamLambdaParameterListNode>(inner.Params);
        Assert.Equal("y", innerParam.Name.Lexeme);

        Assert.IsNotType<LambdaFormExpressionNode>(inner.Body);
        Assert.Null(FindFirst<LambdaExpressionNode>(inner.Body));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Switch-arm guard never captures the arm's trailing `=>`
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

    [Fact]
    public void SwitchArm_IdentifierGuard_DoesNotCaptureTrailingLambda()
    {
        var arms = ParseSwitchArms("value switch { int n when predicate => n.ToString(), _ => \"none\" }");

        _ = Assert.Single(arms.SwitchArms); // first arm + 1 more = 2 total
        var firstArm = arms.SwitchArm;
        var secondArm = arms.SwitchArms[0];

        var typePattern = FindFirst<TypePatternPrimaryPatternNode>(firstArm.Pattern);
        Assert.NotNull(typePattern);
        Assert.Equal("n", typePattern!.Name.Lexeme);

        Assert.NotNull(firstArm.Guard);

        // Condition is a VoidCoalescingNode, which sits below the lambda alternative, so the
        // `=>` is left for the arm.
        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Guard!.Condition));
        var guardIdent = FindFirst<IdentifierExpressionPrimaryNode>(firstArm.Guard.Condition);
        Assert.NotNull(guardIdent);
        Assert.Equal("predicate", guardIdent!.Name.Lexeme);

        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Result));
        var resultCall = FindFirst<MethodCallSuffixPostfixSuffixNode>(firstArm.Result);
        Assert.NotNull(resultCall);
        Assert.Equal("ToString", resultCall!.Name.Lexeme);
        var resultTarget = FindFirst<IdentifierExpressionPrimaryNode>(firstArm.Result);
        Assert.NotNull(resultTarget);
        Assert.Equal("n", resultTarget!.Name.Lexeme);

        Assert.NotNull(FindFirst<DiscardPatternPrimaryPatternNode>(secondArm.Pattern));
        Assert.Null(secondArm.Guard);
        Assert.NotNull(FindFirst<StringLiteralExpressionPrimaryNode>(secondArm.Result));
    }

    [Fact]
    public void SwitchArm_RelationalGuard_ParsesGuardAndResultCorrectly()
    {
        var arms = ParseSwitchArms("value switch { int n when n > 7 => n.ToString(), _ => \"none\" }");

        _ = Assert.Single(arms.SwitchArms);
        var firstArm = arms.SwitchArm;

        Assert.NotNull(firstArm.Guard);
        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Guard!.Condition));

        var comparison = FindFirst<RelationalComparisonRelationalNode>(firstArm.Guard.Condition);
        Assert.NotNull(comparison);
        Assert.Equal(">", comparison!.Group0[^1].Operator.Lexeme);

        var resultCall = FindFirst<MethodCallSuffixPostfixSuffixNode>(firstArm.Result);
        Assert.NotNull(resultCall);
        Assert.Equal("ToString", resultCall!.Name.Lexeme);
    }

    [Fact]
    public void SwitchArm_InvocationGuard_ParsesGuardAndResultCorrectly()
    {
        var arms = ParseSwitchArms("value switch { int n when isOdd(n) => n.ToString(), _ => \"none\" }");

        _ = Assert.Single(arms.SwitchArms);
        var firstArm = arms.SwitchArm;

        Assert.NotNull(firstArm.Guard);
        Assert.Null(FindFirst<LambdaExpressionNode>(firstArm.Guard!.Condition));

        Assert.NotNull(FindFirst<InvocationSuffixPostfixSuffixNode>(firstArm.Guard.Condition));
        var guardTarget = FindFirst<IdentifierExpressionPrimaryNode>(firstArm.Guard.Condition);
        Assert.NotNull(guardTarget);
        Assert.Equal("isOdd", guardTarget!.Name.Lexeme);

        var resultCall = FindFirst<MethodCallSuffixPostfixSuffixNode>(firstArm.Result);
        Assert.NotNull(resultCall);
        Assert.Equal("ToString", resultCall!.Name.Lexeme);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Non-lambda value positions must NOT parse as lambdas
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