// Tier 1: exceptional literal inputs cannot manufacture a refinement proof.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

public sealed class LiteralPredicateEvaluatorTests
{
    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void ANonfiniteDouble_ProvidesNoLiteralFact(double value)
    {
        var type = new ClassType("Double", "Standard", null, null, null);
        Assert.Null(LiteralValue.Of(new LiteralExpression(null!, value, type)));
        var target = new RefinedType(type, null!, "Positive")
        {
            Predicate = new ComparisonPattern(null!, type, ComparisonKind.GreaterThan, new LiteralExpression(null!, 0.0, type)),
        };
        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue(value, type.FullyQualifiedName)));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ANonfiniteSingle_ProvidesNoLiteralFact(float value)
    {
        var type = new ClassType("Single", "Standard", null, null, null);
        Assert.Null(LiteralValue.Of(new LiteralExpression(null!, value, type)));
    }

    [Theory]
    [InlineData("\\q")]
    [InlineData("unescaped\"quote")]
    public void AnInvalidLiteralBody_ProvidesNoDecodedValue(string body)
    {
        Assert.Null(LiteralValue.Decoded(body));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OnlyAnIntrinsicUnaryOperator_PreservesTheLiteralDecision(bool intrinsic)
    {
        var type = new ClassType("Double", "Standard", null, null, null);
        var operand = new LiteralExpression(null!, 0.5, type);
        var unary = new UnaryExpression(null!, UnaryOperatorKind.Negate, operand, type)
        {
            Operator = new Operator(OperatorKind.Minus, type, [], new MemberModifiers { IsIntrinsic = intrinsic }),
        };
        Assert.Equal(intrinsic, LiteralValue.Of(unary) is not null);
    }

    [Theory]
    [InlineData(true, null)]
    [InlineData(false, true)]
    public void AReachableUnsupportedRegexBranch_RemainsUnproven(bool regexFirst, bool? expected)
    {
        var type = new ClassType("String", "Standard", null, null, null);
        var invalid = new RegexPattern(null!, type, "[");
        var satisfied = new ComparisonPattern(null!, type, ComparisonKind.Equal, new LiteralExpression(null!, "x", type));
        var target = new RefinedType(type, null!, "Choice")
        {
            Predicate = new DisjunctionPattern(null!, type, regexFirst ? [invalid, satisfied] : [satisfied, invalid]),
        };
        Assert.Equal(expected, LiteralPredicates.Decide(target, new LiteralValue("x", type.FullyQualifiedName)));
    }

    [Fact]
    public void AMalformedRegex_ProvidesNoPredicateDecision()
    {
        var type = new ClassType("String", "Standard", null, null, null);
        var target = new RefinedType(type, null!, "Invalid")
        {
            Predicate = new RegexPattern(null!, type, "["),
        };
        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue("a", type.FullyQualifiedName)));
    }
}