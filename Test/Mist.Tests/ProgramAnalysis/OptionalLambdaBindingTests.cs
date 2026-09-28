using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class OptionalLambdaBindingTests
{
    [Theory]
    [InlineData("local", "suffix", false, false)]
    [InlineData("local", "suffix", true, true)]
    [InlineData("assignment", "union", false, true)]
    [InlineData("assignment", "union", true, false)]
    [InlineData("field", "alias", false, false)]
    [InlineData("field", "alias", true, true)]
    [InlineData("return", "optional-alias", false, true)]
    [InlineData("return", "optional-alias", true, false)]
    [InlineData("argument", "suffix", false, false)]
    [InlineData("argument", "suffix", true, true)]
    public void ExistingTargetPathsBindThePresentSignature(string site, string spelling, bool block, bool pure)
    {
        var f = new OptionalLambdaFragments(spelling, pure);
        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());

        var bound = f.BindAt(site, OptionalLambdaFragments.Lambda(block));
        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());
        var lambda = Assert.IsType<LambdaExpression>(bound);
        Assert.Same(f.Signature, lambda.Type);
        Assert.Equal(pure, ((IFunctionType)lambda.Type).IsPure);
        Assert.False(lambda.IsPure);
        Assert.Same(f.Binding.Built.Type("String"), Assert.Single(lambda.Parameters).Type);
        Assert.Equal(ConversionKind.Implicit, ConversionClassifier.Classify(lambda.Type, f.Target, f.Binding.Built.Interner));
        var result = block ? Assert.IsType<ReturnStatement>(Assert.Single(lambda.Body!.Statements)).Value : lambda.Result;
        Assert.Same(lambda.Parameters[0], Assert.IsType<LocalReferenceExpression>(result).Local);
        Assert.Null(f.Binding.Binding.TargetType);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresentSignatureSuppliesTheDeclaredResultEvenWhenTheBodyProducesItsPresentValue(bool block)
    {
        var f = new OptionalLambdaFragments();
        var interner = f.Binding.Built.Interner;
        var resultType = interner.GetOrCreateUnionType([f.Binding.Built.Type("String"), f.Binding.Built.Type("Void")]);
        var signature = interner.GetOrCreateFunctionType([f.Binding.Built.Type("String")], resultType);
        f.Binding.Binding.TargetType = interner.GetOrCreateUnionType([signature, f.Binding.Built.Type("Void")]);

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(OptionalLambdaFragments.Lambda(block), f.Binding.Binding, f.Binding.Builder));

        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());
        Assert.Same(signature, lambda.Type);
        Assert.Same(resultType, ((IFunctionType)lambda.Type).ReturnType);
        if(block)
        {
            Assert.Same(resultType, Assert.IsType<ReturnStatement>(Assert.Single(lambda.Body!.Statements)).TargetType);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PresentResultSignatureContextuallyTypesANestedLambda(bool block)
    {
        var f = new OptionalLambdaFragments();
        var interner = f.Binding.Built.Interner;
        var signature = interner.GetOrCreateFunctionType([f.Binding.Built.Type("String")], f.Signature);
        f.Binding.Binding.TargetType = interner.GetOrCreateUnionType([signature, f.Binding.Built.Type("Void")]);
        var syntax = OptionalLambdaFragments.Lambda(block, OptionalLambdaFragments.Lambda(false));

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(syntax, f.Binding.Binding, f.Binding.Builder));

        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());
        Assert.Same(signature, lambda.Type);
        var result = block ? Assert.IsType<ReturnStatement>(Assert.Single(lambda.Body!.Statements)).Value : lambda.Result;
        Assert.Same(f.Signature, Assert.IsType<LambdaExpression>(result).Type);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void DirectNonoptionalTargetsRetainTheirExistingBehavior(bool block, bool pure)
    {
        var f = new OptionalLambdaFragments(pure: pure);
        f.Binding.Binding.TargetType = f.Signature;

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(OptionalLambdaFragments.Lambda(block), f.Binding.Binding, f.Binding.Builder));

        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());
        Assert.Same(f.Signature, lambda.Type);
        Assert.Same(f.Binding.Built.Type("String"), Assert.Single(lambda.Parameters).Type);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AnIncompatibleBodyKeepsTheResultTargetForTheExistingAssignabilityCheck(bool block)
    {
        var f = new OptionalLambdaFragments();
        f.Binding.Binding.TargetType = f.Target;
        var body = BindingFragments.Expression(new TrueLiteralExpressionPrimaryNode());

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(OptionalLambdaFragments.Lambda(block, body), f.Binding.Binding, f.Binding.Builder));

        Assert.Same(f.Signature, lambda.Type);
        var result = block ? Assert.IsType<ReturnStatement>(Assert.Single(lambda.Body!.Statements)).Value : lambda.Result;
        Assert.Same(f.Binding.Built.Type("Boolean"), result!.Type);
        Assert.Same(f.Binding.Built.Type("String"), ((IFunctionType)lambda.Type).ReturnType);
        if(block)
        {
            Assert.Same(f.Binding.Built.Type("String"), Assert.IsType<ReturnStatement>(Assert.Single(lambda.Body!.Statements)).TargetType);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OptionalTargetPreservesTheArityRefusal(bool block)
    {
        var f = new OptionalLambdaFragments();
        f.Binding.Binding.TargetType = f.Target;
        var syntax = OptionalLambdaFragments.Lambda(block);
        if(syntax.Lambda is ExpressionLambdaLambdaExpressionNode expression)
        {
            expression.Params = new ParenthesizedLambdaParamsLambdaParameterListNode();
        }
        else
        {
            ((BlockLambdaLambdaExpressionNode)syntax.Lambda).Params = new ParenthesizedLambdaParamsLambdaParameterListNode();
        }

        _ = Assert.IsType<ErrorExpression>(ExpressionBinder.BindExpression(syntax, f.Binding.Binding, f.Binding.Builder));
        Assert.Equal("MIST_PM_LAMBDA_ARITY", Assert.Single(f.Binding.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LocalLambdaStillConflictsWithAPureOptionalTarget(bool block)
    {
        var f = new OptionalLambdaFragments("optional-alias", pure: true);
        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());

        var lambda = Assert.IsType<LambdaExpression>(f.BindAt("local", OptionalLambdaFragments.Lambda(block, local: true)));

        Assert.True(lambda.IsLocal);
        Assert.True(((IFunctionType)lambda.Type).IsPure);
        Assert.Equal("MIST_PM_PURE_AND_LOCAL", Assert.Single(f.Binding.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Theory]
    [InlineData("none")]
    [InlineData("two-callables")]
    [InlineData("callable-and-noncallable")]
    [InlineData("two-callables-and-void")]
    public void AnImplicitParameterStillNeedsAnUnambiguousContextualTarget(string shape)
    {
        var f = new OptionalLambdaFragments();
        var second = new FunctionType([f.Binding.Built.Type("Boolean")], f.Binding.Built.Type("Boolean"));
        f.Binding.Binding.TargetType = shape switch
        {
            "none" => null,
            "two-callables" => new UnionType([f.Signature, second]),
            "callable-and-noncallable" => new UnionType([f.Signature, f.Binding.Built.Type("String")]),
            _ => new UnionType([f.Signature, second, f.Binding.Built.Type("Void")]),
        };

        _ = Assert.IsType<ErrorExpression>(ExpressionBinder.BindExpression(OptionalLambdaFragments.Lambda(false), f.Binding.Binding, f.Binding.Builder));
        Assert.Equal("MIST_PM_LAMBDA_PARAMETER_NO_TYPE", Assert.Single(f.Binding.Diagnostics.ToImmutableArray()).Id.Value);
    }

    [Fact]
    public void UntargetedExplicitParametersStillInferTheFunctionType()
    {
        var f = new OptionalLambdaFragments();
        var syntax = new LambdaFormExpressionNode
        {
            Lambda = BindingFragments.Lambda(BindingFragments.Expression(BindingFragments.Identifier("x")), "String"),
        };

        var lambda = Assert.IsType<LambdaExpression>(ExpressionBinder.BindExpression(syntax, f.Binding.Binding, f.Binding.Builder));

        Assert.Same(f.Signature, lambda.Type);
        Assert.Empty(f.Binding.Diagnostics.ToImmutableArray());
    }

    [Fact]
    public void OptionalReceiverDoesNotAcquireAFunctionInvocationSignature()
    {
        var f = new OptionalLambdaFragments();
        _ = f.Binding.Local("transform", f.Target);

        var call = Assert.IsType<InvocationExpression>(f.Binding.Bind(BindingFragments.Identifier("transform"),
            new InvocationSuffixPostfixSuffixNode { Arguments = BindingFragments.Arguments(BindingFragments.Expression(new StringLiteralExpressionPrimaryNode { StringLiteral = BindingFragments.Token("\"text\"") })) }));

        Assert.Null(call.ParameterTypes);
        Assert.Same(f.Target, call.Type);
    }
}