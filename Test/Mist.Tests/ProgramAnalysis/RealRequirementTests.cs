// Tier 2: the bound-predicate reader retains inherited requirements and refuses incomplete or unsafe readings.

using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

public sealed class RealRequirementTests
{
    [Fact]
    public void InheritedAliasPredicatesAllConstrainTheSet()
    {
        var real = Type("Double");
        var positive = Refined(real, Comparison(real, ComparisonKind.GreaterThan, 0.0), "Positive");
        var alias = new TypeAlias("Alias", "Test", null, null, null!) { AliasedType = positive };
        var bounded = Refined(alias, Comparison(real, ComparisonKind.LessThanOrEqual, 1.0), "Bounded");
        var set = RealRequirement.OfType(bounded)!;
        Assert.False(set.Contains(Value(0.0, real)));
        Assert.True(set.Contains(Value(double.Epsilon, real)));
        Assert.True(set.Contains(Value(1.0, real)));
        Assert.False(set.Contains(Value(double.BitIncrement(1.0), real)));
        Assert.False(set.IncludesNaN);
        Assert.False(set.IncludesPositiveInfinity);
        Assert.False(set.IncludesNegativeInfinity);
    }

    [Fact]
    public void PatternAndOrAndNotHaveSetSemanticsIncludingNaN()
    {
        var real = Type("Double");
        var one = Comparison(real, ComparisonKind.Equal, 1.0);
        var two = Comparison(real, ComparisonKind.Equal, 2.0);
        var either = new DisjunctionPattern(null!, real, [one, two]);
        var positive = Comparison(real, ComparisonKind.GreaterThan, 0.0);
        var both = new ConjunctionPattern(null!, real, [either, positive]);
        var set = RealRequirement.Of(both, RealKind.Double)!;
        Assert.True(set.Contains(Value(1.0, real)));
        Assert.True(set.Contains(Value(2.0, real)));
        Assert.False(set.Contains(Value(1.5, real)));
        Assert.False(set.IncludesNaN);
        var notPositive = RealRequirement.Of(new NegatedPattern(null!, real, positive), RealKind.Double)!;
        Assert.True(notPositive.IncludesNaN);
        Assert.True(notPositive.Contains(Value(0.0, real)));
        Assert.False(notPositive.Contains(Value(double.Epsilon, real)));
    }

    [Fact]
    public void ADoubleBoundOverSingleNeverRoundsDownToSingle()
    {
        var single = Type("Single");
        var bound = new LiteralExpression(null!, 0.1, Type("Double"));
        var predicate = new ComparisonPattern(null!, single, ComparisonKind.LessThanOrEqual, bound);
        var set = RealRequirement.Of(predicate, RealKind.Single)!;
        Assert.False(set.Contains(Value(0.1f, single)));
        Assert.True(set.Contains(Value(float.BitDecrement(0.1f), single)));
        Assert.Equal(RealSet.Compare(RealKind.Single, ComparisonKind.LessThanOrEqual, Value(0.1, Type("Double"))), set);
    }

    [Fact]
    public void UnsupportedOperandsNeverDisappearFromARequirement()
    {
        var real = Type("Double");
        var known = Comparison(real, ComparisonKind.GreaterThan, 0.0);
        var unknown = new RegexPattern(null!, real, "x");
        Assert.Null(RealRequirement.Of(unknown, RealKind.Double));
        Assert.Null(RealRequirement.Of(new ConjunctionPattern(null!, real, [known, unknown]), RealKind.Double));
        Assert.Null(RealRequirement.Of(new DisjunctionPattern(null!, real, [known, unknown]), RealKind.Double));
        Assert.Null(RealRequirement.Of(new NegatedPattern(null!, real, unknown), RealKind.Double));
        Assert.Null(RealRequirement.OfType(Refined(Refined(real, unknown, "Unknown"), known, "KnownAboveUnknown")));
        Assert.Null(RealRequirement.OfType(new RefinedType(real, null!, "Unbound")));
        var symbolic = new LocalReferenceExpression(null!, new LocalVariable("limit", real));
        Assert.Null(RealRequirement.Of(new ComparisonPattern(null!, real, ComparisonKind.LessThan, symbolic), RealKind.Double));
    }

    [Fact]
    public void TypeTestsRequireTheSameRuntimeDomainAndReadablePredicate()
    {
        var real = Type("Double");
        var positive = Comparison(real, ComparisonKind.GreaterThan, 0.0);
        var refined = Refined(real, positive, "Positive");
        var typed = new TypeTestPattern(null!, refined, null, refinement: positive);
        Assert.Equal(RealRequirement.OfType(refined), RealRequirement.Of(typed, RealKind.Double));
        var boundedPredicate = Comparison(real, ComparisonKind.LessThanOrEqual, 1.0);
        var bounded = Refined(refined, boundedPredicate, "Bounded");
        var boundedTest = new TypeTestPattern(null!, bounded, null, refinement: boundedPredicate);
        Assert.False(RealRequirement.Of(boundedTest, RealKind.Double)!.Contains(Value(0.0, real)));
        Assert.Equal(RealSet.Everything(RealKind.Double), RealRequirement.Of(new TypeTestPattern(null!, real, null), RealKind.Double));
        Assert.Null(RealRequirement.Of(new TypeTestPattern(null!, Type("Single"), null), RealKind.Double));
        Assert.Equal(RealSet.Everything(RealKind.Double), RealRequirement.Of(new CatchAllPattern(null!, real, null), RealKind.Double));
        Assert.Null(RealRequirement.Of(null, RealKind.Double));
    }

    [Fact]
    public void LocalCopiesUseSymbolIdentityAndDoNotInventMissingFacts()
    {
        var real = Type("Double");
        var first = new LocalVariable("value", real);
        var second = new LocalVariable("value", real);
        var fact = RealSet.Compare(RealKind.Double, ComparisonKind.GreaterThan, Value(0.0, real))!;
        RealSet? held(LocalVariable local)
        {
            return ReferenceEquals(local, first) ? fact : null;
        }

        Assert.Equal(fact, RealRequirement.OfValue(new CloneExpression(null!, new LocalReferenceExpression(null!, first)), held));
        Assert.Null(RealRequirement.OfValue(new LocalReferenceExpression(null!, second), held));
        Assert.Null(RealRequirement.OfValue(new LocalReferenceExpression(null!, first)));
    }

    [Fact]
    public void ArithmeticAndUserUnaryOperatorsProvideNoValueFact()
    {
        var real = Type("Double");
        var operand = new LiteralExpression(null!, 1.0, real);
        var sum = new BinaryExpression(null!, operand, BinaryOperatorKind.Add, operand, real);
        Assert.Null(RealRequirement.OfValue(sum));
        Assert.Null(RealRequirement.LiteralOf(sum));
        var unary = new UnaryExpression(null!, UnaryOperatorKind.Negate, operand, real)
        {
            Operator = new Operator(OperatorKind.Minus, real, [], new MemberModifiers { IsIntrinsic = false }),
        };
        Assert.Null(RealRequirement.OfValue(unary));
        Assert.Null(RealRequirement.LiteralOf(unary));
        var intrinsic = new UnaryExpression(null!, UnaryOperatorKind.Negate, operand, real)
        {
            Operator = new Operator(OperatorKind.Minus, real, [], new MemberModifiers { IsIntrinsic = true }),
        };
        Assert.True(RealRequirement.OfValue(intrinsic)!.Contains(Value(-1.0, real)));
    }

    [Fact]
    public void BinaryNonfiniteLiteralNodesRemainExplicitFacts()
    {
        foreach(var number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            var real = Type("Double");
            var literal = new LiteralExpression(null!, number, real);
            var set = RealRequirement.OfValue(literal)!;
            Assert.True(set.Contains(Value(number, real)));
            Assert.Null(LiteralValue.Of(literal));
            Assert.Null(LiteralPredicates.Decide(Refined(real, Comparison(real, ComparisonKind.GreaterThan, 0.0), "Positive"), RealRequirement.LiteralOf(literal)));
            var negated = new UnaryExpression(null!, UnaryOperatorKind.Negate, literal, real);
            Assert.True(RealRequirement.OfValue(negated)!.Contains(Value(-number, real)));
        }

        var single = Type("Single");
        Assert.True(RealRequirement.OfValue(new LiteralExpression(null!, float.NaN, single))!.IncludesNaN);
        Assert.True(RealRequirement.OfValue(new LiteralExpression(null!, float.PositiveInfinity, single))!.IncludesPositiveInfinity);
        Assert.True(RealRequirement.OfValue(new LiteralExpression(null!, float.NegativeInfinity, single))!.IncludesNegativeInfinity);
    }

    [Fact]
    public void NonfinitePatternBounds_StayUnreadableUntilTheirEmissionIsSupported()
    {
        foreach(var number in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        {
            foreach(var kind in new[] { ComparisonKind.Equal, ComparisonKind.LessThan })
            {
                Assert.Null(RealRequirement.Of(Comparison(Type("Double"), kind, number), RealKind.Double));
                Assert.Null(RealRequirement.Of(Comparison(Type("Single"), kind, (float)number), RealKind.Single));
            }
        }
    }

    [Fact]
    public void PlainRealTypesHaveTheirRuntimeUniverseWithoutIntegralOrStringFacts()
    {
        Assert.Equal(RealSet.Everything(RealKind.Single), RealRequirement.OfType(Type("Single")));
        Assert.Equal(RealSet.Everything(RealKind.Double), RealRequirement.OfType(Type("Double")));
        Assert.Equal(RealSet.Everything(RealKind.Decimal), RealRequirement.OfType(Type("Decimal")));
        Assert.Null(RealRequirement.OfType(Type("Int32")));
        Assert.Null(RealRequirement.OfType(Type("String")));
        Assert.Null(RealRequirement.OfValue(new LiteralExpression(null!, 1, Type("Int32"))));
    }

    private static ClassType Type(string name)
    {
        return new ClassType(name, "Standard", null, null, null);
    }

    private static LiteralValue Value(object value, IMistType type)
    {
        return new LiteralValue(value, type.FullyQualifiedName);
    }

    private static ComparisonPattern Comparison(IMistType type, ComparisonKind kind, object value)
    {
        return new ComparisonPattern(null!, type, kind, new LiteralExpression(null!, value, type));
    }

    private static RefinedType Refined(IMistType type, Pattern predicate, string name)
    {
        return new RefinedType(type, null!, name) { Predicate = predicate };
    }
}