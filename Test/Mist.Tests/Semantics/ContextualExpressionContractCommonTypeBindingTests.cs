using Mist.Generated;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Construction.Binding.Expressions;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "2")]
public sealed class ContextualExpressionContractCommonTypeBindingTests
{
    [Theory]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("switch", false)]
    [InlineData("switch", true)]
    [InlineData("lambda", false)]
    [InlineData("lambda", true)]
    public void UntargetedValuesWidenInEitherOrder(string consumer, bool reverse)
    {
        var fragments = Fragments();
        _ = fragments.Local("first", fragments.Built.Type("Int32"));
        _ = fragments.Local("second", fragments.Built.Type("Int64"));

        var bound = Bind(consumer, fragments, reverse);

        Assert.Same(fragments.Built.Type("Int64"), ResultType(bound));
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
        if(bound is LambdaExpression lambda)
        {
            Assert.All(LambdaBinder.Returns(lambda.Body!), returned => Assert.Same(fragments.Built.Type("Int64"), returned.TargetType));
        }
    }

    [Theory]
    [InlineData("array")]
    [InlineData("switch")]
    [InlineData("lambda")]
    public void NominalSiblingValuesUseTheirSharedBase(string consumer)
    {
        var fragments = Fragments();
        var parent = HandBuiltModel.Class("Parent");
        _ = fragments.Local("first", HandBuiltModel.Class("First", parent));
        _ = fragments.Local("second", HandBuiltModel.Class("Second", parent));

        Assert.Same(parent, ResultType(Bind(consumer, fragments)));
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("array", false)]
    [InlineData("array", true)]
    [InlineData("switch", false)]
    [InlineData("switch", true)]
    [InlineData("lambda", false)]
    [InlineData("lambda", true)]
    public void InferenceRecordsEachDeclaredImplicitConversion(string consumer, bool reverse)
    {
        var fragments = Fragments();
        var target = HandBuiltModel.Class("Target");
        var first = HandBuiltModel.Class("First");
        var second = HandBuiltModel.Class("Second");
        var firstConversion = HandBuiltModel.Conversion(first, first, target);
        var secondConversion = HandBuiltModel.Conversion(second, second, target);
        _ = fragments.Local("first", first);
        _ = fragments.Local("second", second);

        var bound = Bind(consumer, fragments, reverse);
        var expected = reverse ? new[] { secondConversion, firstConversion } : [firstConversion, secondConversion];

        Assert.Same(target, ResultType(bound));
        Assert.Equal(expected,
            Values(bound).Select(value => Assert.IsType<ImplicitConversionExpression>(value).Operator));
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("array", false)]
    [InlineData("switch", false)]
    [InlineData("lambda", false)]
    [InlineData("array", true)]
    [InlineData("switch", true)]
    [InlineData("lambda", true)]
    public void SameTypesAndExistingUnionConstituentsRetainTheirSupportedResult(string consumer, bool union)
    {
        var fragments = Fragments();
        IMistType common = union
            ? new UnionType([fragments.Built.Type("Int32"), fragments.Built.Type("String")])
            : fragments.Built.Type("Int32");
        _ = fragments.Local("first", fragments.Built.Type("Int32"));
        _ = fragments.Local("second", common);

        Assert.Same(common, ResultType(Bind(consumer, fragments)));
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("array")]
    [InlineData("switch")]
    [InlineData("lambda")]
    public void AnExplicitTargetStillSuppliesTheResult(string consumer)
    {
        var fragments = Fragments();
        var target = fragments.Built.Type("Double");
        _ = fragments.Local("first", fragments.Built.Type("Int32"));
        _ = fragments.Local("second", fragments.Built.Type("Int64"));
        var supplied = consumer switch
        {
            "array" => fragments.Builder.Resolver.ConstructArray(target, null),
            "lambda" => new FunctionType([], target),
            _ => target,
        };
        Expression bound;
        using(fragments.Binding.PushTargetType(supplied))
        {
            bound = Bind(consumer, fragments);
        }

        Assert.Same(target, ResultType(bound));
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("array")]
    [InlineData("switch")]
    [InlineData("lambda")]
    public void AReportedInputDoesNotCauseACommonTypeDiagnostic(string consumer)
    {
        var fragments = Fragments();
        _ = fragments.Local("second", fragments.Built.Type("Int32"));

        var bound = Bind(consumer, fragments);

        Assert.Same(fragments.Built.Type("Int32"), ResultType(bound));
        var diagnostic = Assert.Single(fragments.Diagnostics.ToImmutableArray());
        Assert.Equal("MIST_PM_UNRESOLVED_IDENTIFIER", diagnostic.Id.Value);
        _ = Assert.IsType<ErrorExpression>(Values(bound).First());
        if(bound is ArrayLiteralExpression or SwitchExpression)
        {
            Assert.True(PriorFailure.Of(bound));
        }
    }

    [Theory]
    [InlineData("array")]
    [InlineData("switch")]
    [InlineData("lambda")]
    public void EntirelyFailedInputsKeepTheirOwnersAndSubtrees(string consumer)
    {
        var fragments = Fragments();

        var bound = Bind(consumer, fragments);

        Assert.Same(ErrorType.Instance, ResultType(bound));
        Assert.All(fragments.Diagnostics.ToImmutableArray(), diagnostic => Assert.Equal("MIST_PM_UNRESOLVED_IDENTIFIER", diagnostic.Id.Value));
        Assert.Equal(2, fragments.Diagnostics.ToImmutableArray().Length);
        if(bound is ErrorExpression error)
        {
            Assert.True(PriorFailure.Of(bound));
            if(consumer == "switch")
            {
                var retained = Assert.IsType<SwitchExpression>(Assert.Single(error.Children));
                Assert.Equal(2, retained.Arms.Count);
                Assert.All(retained.Arms, arm => Assert.IsType<ErrorExpression>(arm.Result));
            }
            else
            {
                Assert.Equal(2, error.Children.Count);
            }
        }
        else
        {
            Assert.Equal(2, Values(bound).Count());
        }
    }

    [Theory]
    [InlineData("array")]
    [InlineData("switch")]
    [InlineData("lambda")]
    public void AnUnfinishedInputStaysInTheModelWithoutBecomingAReportedFailure(string consumer)
    {
        var fragments = Fragments();
        _ = fragments.Local("first", ErrorType.Instance);
        _ = fragments.Local("second", fragments.Built.Type("Int32"));

        var bound = Bind(consumer, fragments);

        Assert.Same(ErrorType.Instance, ResultType(bound));
        Assert.False(PriorFailure.Of(bound));
        _ = Assert.IsType<LocalReferenceExpression>(Values(bound).First());
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    [Theory]
    [InlineData("array", "MIST_PM_ARRAY_LITERAL_NO_COMMON_TYPE")]
    [InlineData("switch", "MIST_PM_SWITCH_ARMS_NO_COMMON_TYPE")]
    [InlineData("lambda", "MIST_PM_LAMBDA_RETURNS_NO_COMMON_TYPE")]
    public void IncompatibleValuesReceiveTheConsumersOwningDiagnostic(string consumer, string id)
    {
        var fragments = Fragments();
        _ = fragments.Local("first", fragments.Built.Type("Int32"));
        _ = fragments.Local("second", fragments.Built.Type("String"));

        Assert.Same(ErrorType.Instance, ResultType(Bind(consumer, fragments)));
        var diagnostic = Assert.Single(fragments.Diagnostics.ToImmutableArray());
        Assert.Equal(id, diagnostic.Id.Value);
        Assert.Equal(45, diagnostic.Location.Start.Column);
    }

    [Fact]
    public void OuterReturnInferenceExcludesNestedLambdasAndLocalFunctions()
    {
        var fragments = Fragments();
        var nestedReturn = new ReturnStatement(HandBuiltModel.Literal(fragments.Built.Type("String")), null);
        var nestedLambda = new LambdaExpression(HandBuiltModel.Node(), [], null, HandBuiltModel.Block(nestedReturn), false,
            new FunctionType([], fragments.Built.Type("String")));
        var functionReturn = new ReturnStatement(HandBuiltModel.Literal(fragments.Built.Type("Boolean")), null);
        var localFunction = new Method("Nested", fragments.Built.Type("Boolean"), [], new MemberModifiers())
        {
            Body = HandBuiltModel.Block(functionReturn),
        };
        var scopedReturn = new ReturnStatement(HandBuiltModel.Literal(fragments.Built.Type("Int64")), null);
        var body = HandBuiltModel.Block(
            new ExpressionStatement(nestedLambda), new LocalFunctionStatement(localFunction, []),
            new WithBlockStatement(HandBuiltModel.Literal(fragments.Built.Type("Int32")), HandBuiltModel.Block(scopedReturn)),
            new ReturnStatement(HandBuiltModel.Literal(fragments.Built.Type("Int32")), null));

        Assert.Same(fragments.Built.Type("Int64"), LambdaBinder.InferredReturnType(body, HandBuiltModel.Node(44), fragments.Builder));
        Assert.Equal(2, LambdaBinder.Returns(body).Count());
        Assert.Null(nestedReturn.TargetType);
        Assert.Null(functionReturn.TargetType);
        Assert.Empty(fragments.Diagnostics.ToImmutableArray());
    }

    private static BindingFragments Fragments()
    {
        var fragments = new BindingFragments();
        fragments.Register(fragments.Built.Type("Array<,>"));
        _ = fragments.Local("condition", fragments.Built.Type("Boolean"));
        return fragments;
    }

    private static Expression Bind(string consumer, BindingFragments fragments, bool reverse = false)
    {
        var first = BindingFragments.Expression(BindingFragments.Identifier(reverse ? "second" : "first"));
        var second = BindingFragments.Expression(BindingFragments.Identifier(reverse ? "first" : "second"));
        if(consumer == "array")
        {
            var literal = BindingFragments.At<ArrayLiteralExpressionPrimaryNode>(44);
            literal.Elements = new ArrayElementListNode { First = first, Expressions = [second] };
            return fragments.Bind(literal);
        }

        if(consumer == "lambda")
        {
            var lambda = BindingFragments.At<BlockLambdaLambdaExpressionNode>(44);
            lambda.Params = new ParenthesizedLambdaParamsLambdaParameterListNode();
            lambda.Body = new BlockNode { Statements = [Return(first), Return(second)] };
            return LambdaBinder.Bind(lambda, fragments.Binding, fragments.Builder);
        }

        var selection = BindingFragments.At<SwitchExpressionSuffixPostfixSuffixNode>(44);
        selection.Arms = new SwitchArmListNode
        {
            SwitchArm = new SwitchArmNode { Pattern = Discard(), Result = first },
            SwitchArms = [new SwitchArmNode { Pattern = Discard(), Result = second }],
        };
        return fragments.Bind(BindingFragments.Identifier("condition"), selection);
    }

    private static PatternNode Discard()
    {
        return new PatternNode
        {
            OrPattern = new OrPatternNode
            {
                Left = new AndPatternNode { Left = new DirectPatternUnaryPatternNode { Primary = new DiscardPatternPrimaryPatternNode() } },
            },
        };
    }

    private static ReturnStatementStatementNode Return(ExpressionNode result)
    {
        return new ReturnStatementStatementNode { ReturnStatement = new ReturnStatementNode { Value = result } };
    }

    private static IMistType ResultType(Expression expression)
    {
        return expression switch
        {
            ArrayLiteralExpression array => array.ElementType,
            LambdaExpression lambda => ((FunctionType)lambda.Type).ReturnType,
            _ => expression.Type,
        };
    }

    private static IEnumerable<Expression> Values(Expression expression)
    {
        return expression switch
        {
            ArrayLiteralExpression array => array.Elements,
            SwitchExpression selection => selection.Arms.Select(arm => arm.Result),
            LambdaExpression lambda => LambdaBinder.Returns(lambda.Body!).Select(returned => returned.Value!),
            _ => [],
        };
    }
}