using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class LambdaResultAndPromiseBindingTests
{
    [Theory]
    [InlineData("else", false)]
    [InlineData("else", true)]
    [InlineData("else-if", false)]
    [InlineData("else-if", true)]
    [InlineData("while", false)]
    [InlineData("while", true)]
    [InlineData("for", false)]
    [InlineData("for", true)]
    [InlineData("foreach", false)]
    [InlineData("foreach", true)]
    [InlineData("with", false)]
    [InlineData("with", true)]
    public void ANestedReturnJoinsTheCommonResultAndIsSettledToIt(string shape, bool nestedWidens)
    {
        var f = new BindingFragments();
        var narrow = HandBuiltModel.Class("Narrow");
        var wide = HandBuiltModel.Class("Wide");
        var conversion = HandBuiltModel.Conversion(narrow, narrow, wide);
        var nested = new ReturnStatement(HandBuiltModel.Literal(nestedWidens ? wide : narrow), null);
        var body = HandBuiltModel.Block(
            Enclosing(shape, nested, f),
            new ReturnStatement(HandBuiltModel.Literal(nestedWidens ? narrow : wide), null));

        Assert.Same(wide, LambdaBinder.InferredReturnType(body, HandBuiltModel.Node(44), f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        var settled = new[] { Nested(shape, body.Statements[0]), body.Statements[1] }.Select(Assert.IsType<ReturnStatement>).ToList();
        Assert.All(settled, returned => Assert.Same(wide, returned.TargetType));
        var converted = settled[nestedWidens ? 1 : 0].Value;
        Assert.Same(conversion, Assert.IsType<ImplicitConversionExpression>(converted).Operator);
        Assert.Same(wide, converted.Type);
        _ = Assert.IsType<LiteralExpression>(settled[nestedWidens ? 0 : 1].Value);
    }

    [Fact]
    public void ABodyWhoseOnlyReturnCarriesNoValueInfersNoResult()
    {
        var f = new BindingFragments();

        Assert.Null(LambdaBinder.InferredReturnType(HandBuiltModel.Block(new ReturnStatement(null, null)), HandBuiltModel.Node(44), f.Builder));
        Assert.Empty(f.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PropagationInAnUntargetedLambdaReturnsTheFailureAsTheInferredResult(bool block)
    {
        var f = new BindingFragments();
        var results = new ResultModel();
        var result = results.Result(results.Integer, results.Text);
        _ = f.Local("read", result);
        _ = f.Local("wrap", new FunctionType([results.Integer], result));
        var propagated = BindingFragments.Expression(BindingFragments.Identifier("read"), BindingFragments.At<PropagationSuffixPostfixSuffixNode>());
        var invocation = BindingFragments.At<InvocationSuffixPostfixSuffixNode>();
        invocation.Arguments = BindingFragments.Arguments(propagated);
        var value = BindingFragments.Expression(BindingFragments.Identifier("wrap"), invocation);
        var syntax = new LambdaFormExpressionNode { Lambda = Lambda(block, value, new ParenthesizedLambdaParamsLambdaParameterListNode()) };

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(syntax, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.Same(result, ((IFunctionType)lambda.Type).ReturnType);
        var body = block ? Assert.IsType<ReturnStatement>(Assert.Single(lambda.Body!.Statements)).Value : lambda.Result;
        var call = Assert.IsType<InvocationExpression>(body);
        var propagation = Assert.IsType<ResultPropagationExpression>(Assert.Single(call.Arguments));
        Assert.Same(result, propagation.FailureReturn.TargetType);
        var failure = Assert.IsType<ResultConstructionExpression>(propagation.FailureReturn.Value);
        Assert.Same(result, failure.Type);
        Assert.Equal(ResultCase.Failure, failure.Case);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APureLambdaInAPlainSlotKeepsThePureTypeAndConvertsOut(bool block)
    {
        var f = new BindingFragments();
        var text = f.Built.Type("String");
        var plain = f.Built.Interner.GetOrCreateFunctionType([text], text);
        f.Binding.TargetType = plain;
        var parameter = new SingleImplicitParamLambdaParameterListNode { Name = BindingFragments.Token("x") };
        var lambdaSyntax = Lambda(block, BindingFragments.Expression(BindingFragments.Identifier("x")), parameter, new PureLambdaModifierLambdaModifierNode());

        var lambda = Assert.IsType<LambdaExpression>(
            ExpressionBinder.BindExpression(new LambdaFormExpressionNode { Lambda = lambdaSyntax }, f.Binding, f.Builder));

        Assert.Empty(f.Diagnostics.ToImmutableArray());
        Assert.True(lambda.IsPure);
        Assert.False(lambda.IsLocal);
        Assert.Same(f.Built.Interner.GetOrCreateFunctionType([text], text, isPure: true), lambda.Type);
        Assert.Same(text, Assert.Single(lambda.Parameters).Type);
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(lambda.Type, plain, f.Built.Interner));
    }

    private static Statement Enclosing(string shape, Statement nested, BindingFragments f)
    {
        var condition = HandBuiltModel.Literal(f.Built.Type("Boolean"));
        var inner = HandBuiltModel.Block(nested);
        return shape switch
        {
            "else" => new IfStatement(condition, HandBuiltModel.Block(), inner),
            "else-if" => new IfStatement(condition, HandBuiltModel.Block(), new IfStatement(condition, inner, null)),
            "while" => new WhileStatement(condition, inner),
            "for" => new ForStatement(null, condition, null, inner),
            "foreach" => new ForEachStatement(new LocalVariable("item", f.Built.Type("Int32")), HandBuiltModel.Literal(f.Built.Type("String")), inner),
            "with" => new WithBlockStatement(HandBuiltModel.Literal(f.Built.Type("Int32")), inner),
            _ => throw new ArgumentOutOfRangeException(nameof(shape)),
        };
    }

    private static Statement Nested(string shape, Statement enclosing)
    {
        var inner = shape switch
        {
            "else" => Assert.IsType<Block>(Assert.IsType<IfStatement>(enclosing).ElseBranch),
            "else-if" => Assert.IsType<IfStatement>(Assert.IsType<IfStatement>(enclosing).ElseBranch).ThenBranch,
            "while" => Assert.IsType<WhileStatement>(enclosing).Body,
            "for" => Assert.IsType<ForStatement>(enclosing).Body,
            "foreach" => Assert.IsType<ForEachStatement>(enclosing).Body,
            _ => Assert.IsType<WithBlockStatement>(enclosing).Body,
        };
        return Assert.Single(inner.Statements);
    }

    private static LambdaExpressionNode Lambda(bool block, ExpressionNode value, LambdaParameterListNode parameters, params LambdaModifierNode[] modifiers)
    {
        LambdaExpressionNode lambda = block
            ? new BlockLambdaLambdaExpressionNode
            {
                Params = parameters,
                Modifiers = [.. modifiers],
                Body = new BlockNode
                {
                    Statements = [new ReturnStatementStatementNode { ReturnStatement = new ReturnStatementNode { Value = value } }],
                },
            }
            : new ExpressionLambdaLambdaExpressionNode { Params = parameters, Modifiers = [.. modifiers], Body = value };
        var location = HandBuiltModel.Node(30);
        lambda.Span = location.Span;
        lambda.Origin = location.Origin;
        return lambda;
    }
}