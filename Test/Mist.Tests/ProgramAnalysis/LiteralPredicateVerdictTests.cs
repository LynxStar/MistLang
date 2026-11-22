using System.Text.RegularExpressions;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Types;

namespace Mist.Tests.ProgramAnalysis;

/// <summary>
/// What <see cref="LiteralPredicates"/> decides for a constant: true and false are a proof and a
/// violation, null leaves the value unproven.
/// </summary>
[Trait("Tier", "1")]
public sealed class LiteralPredicateVerdictTests
{
    private static readonly ClassType Text = new("String", "Standard", null, null, null);

    private static readonly ClassType Real = new("Double", "Standard", null, null, null);

    private static readonly ClassType Float = new("Single", "Standard", null, null, null);

    private static readonly ClassType Money = new("Decimal", "Standard", null, null, null);

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ANonfiniteSingle_GetsNoDecisionEvenWhereItsOrderingWouldSatisfyTheBound(float value)
    {
        var target = Refined(Float, Comparison(Float, ComparisonKind.LessThan, 1.0f));

        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue(value, Float.FullyQualifiedName)));
    }

    [Theory]
    [InlineData(nameof(ComparisonKind.Equal), false, true, false)]
    [InlineData(nameof(ComparisonKind.LessThan), true, false, false)]
    [InlineData(nameof(ComparisonKind.LessThanOrEqual), true, true, false)]
    [InlineData(nameof(ComparisonKind.GreaterThan), false, false, true)]
    [InlineData(nameof(ComparisonKind.GreaterThanOrEqual), false, true, true)]
    public void ARealComparison_DecidesByItsKindAgainstTheBound(string kind, bool below, bool at, bool above)
    {
        var cases = new (ClassType Type, object Bound, object[] Values)[]
        {
            (Real, 1.0, [0.5, 1.0, 1.5]),
            (Float, 1.0f, [0.5f, 1.0f, 1.5f]),
            (Money, 1.0m, [0.5m, 1.0m, 1.5m]),
        };
        bool?[] expected = [below, at, above];

        foreach(var (type, bound, values) in cases)
        {
            var target = Refined(type, Comparison(type, Enum.Parse<ComparisonKind>(kind), bound));

            for(var index = 0; index < values.Length; index++)
            {
                var decided = LiteralPredicates.Decide(target, new LiteralValue(values[index], type.FullyQualifiedName));
                Assert.True(expected[index] == decided, $"{type.Name} {values[index]} {kind} {bound}: expected {expected[index]}, got {decided}");
            }
        }
    }

    [Fact]
    public void ADecimalAgainstADoubleBound_GetsNoDecision()
    {
        var target = Refined(Money, Comparison(Money, ComparisonKind.LessThanOrEqual, 0.5));

        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue(0.25m, Money.FullyQualifiedName)));
        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue(0.75m, Money.FullyQualifiedName)));
    }

    [Fact]
    public void ABoundWithoutALiteralReading_GetsNoDecision()
    {
        var target = Refined(Real, Comparison(Real, ComparisonKind.LessThan, double.NaN));

        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue(1.0, Real.FullyQualifiedName)));
    }

    [Theory]
    [InlineData("x", "x", true)]
    [InlineData("X", "x", false)]
    [InlineData("x ", "x", false)]
    [InlineData("e\\u0301", "\\u00e9", false)]
    [InlineData("\\u00e9", "\\u00e9", true)]
    public void AStringEquality_ComparesOrdinally(string value, string bound, bool expected)
    {
        var target = Refined(Text, Comparison(Text, ComparisonKind.Equal, bound));
        var text = LiteralValue.Decoded(value)!;

        Assert.Equal(expected, LiteralPredicates.Decide(target, new LiteralValue(text, Text.FullyQualifiedName)));
    }

    [Theory]
    [InlineData(nameof(ComparisonKind.LessThan), "a")]
    [InlineData(nameof(ComparisonKind.GreaterThanOrEqual), "a")]
    [InlineData(nameof(ComparisonKind.LessThanOrEqual), "b")]
    public void ARelationalStringComparison_GetsNoDecision(string kind, string value)
    {
        var target = Refined(Text, Comparison(Text, Enum.Parse<ComparisonKind>(kind), "b"));

        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue(value, Text.FullyQualifiedName)));
    }

    [Fact]
    public void AStringEqualityAgainstANonStringBound_GetsNoDecision()
    {
        var target = Refined(Text, Comparison(Text, ComparisonKind.Equal, 1.0));

        Assert.Null(LiteralPredicates.Decide(target, new LiteralValue("1", Text.FullyQualifiedName)));
    }

    [Theory]
    [InlineData(2.5, true)]
    [InlineData(-2.5, false)]
    public void AnAlias_IsDecidedByTheRefinementItNames(double value, bool expected)
    {
        var alias = Alias(Refined(Real, Comparison(Real, ComparisonKind.GreaterThan, 0.0)));

        Assert.Equal(expected, LiteralPredicates.Decide(alias, new LiteralValue(value, Real.FullyQualifiedName)));
    }

    [Fact]
    public void ATargetWithNoRefinementToRead_GetsNoDecision()
    {
        var value = new LiteralValue(2.5, Real.FullyQualifiedName);

        Assert.Null(LiteralPredicates.Decide(Alias(null), value));
        Assert.Null(LiteralPredicates.Decide(Real, value));
    }

    [Theory]
    [InlineData("1")]
    [InlineData(null)]
    public void AValueThatDoesNotConvertToTheBase_GetsNoDecision(string? text)
    {
        var target = Refined(Real, Comparison(Real, ComparisonKind.GreaterThan, 0.0));
        var value = text is null ? new LiteralValue(1.0m, Money.FullyQualifiedName) : new LiteralValue(text, Text.FullyQualifiedName);

        Assert.Null(LiteralPredicates.Decide(target, value));
    }

    [Theory]
    [InlineData("decided", "decided", 5.0, true)]
    [InlineData("decided", "decided", -1.0, false)]
    [InlineData("decided", "decided", 20.0, false)]
    [InlineData("undecided", "decided", 20.0, false)]
    [InlineData("undecided", "decided", 5.0, null)]
    [InlineData("decided", "undecided", -1.0, false)]
    [InlineData("decided", "undecided", 5.0, null)]
    [InlineData("aliased", "decided", -1.0, false)]
    [InlineData("aliased", "decided", 5.0, true)]
    public void ARefinementOverARefinedBase_NeedsBothToHold(string inner, string outer, double value, bool? expected)
    {
        var positive = Refined(Real, inner == "undecided"
            ? Comparison(Real, ComparisonKind.GreaterThan, double.NaN)
            : Comparison(Real, ComparisonKind.GreaterThan, 0.0));
        var target = Refined(inner == "aliased" ? Alias(positive) : positive, outer == "undecided"
            ? Comparison(Real, ComparisonKind.LessThanOrEqual, double.NaN)
            : Comparison(Real, ComparisonKind.LessThanOrEqual, 10.0));

        Assert.Equal(expected, LiteralPredicates.Decide(target, new LiteralValue(value, Real.FullyQualifiedName)));
    }

    [Theory]
    [InlineData("TT", true)]
    [InlineData("TF", false)]
    [InlineData("FU", false)]
    [InlineData("UF", null)]
    [InlineData("TU", null)]
    [InlineData("TTF", false)]
    public void AConjunction_TakesItsFirstOperandThatIsNotTrue(string operands, bool? expected)
    {
        var target = Refined(Text, new ConjunctionPattern(null!, Text, Operands(operands)));

        Assert.Equal(expected, LiteralPredicates.Decide(target, new LiteralValue("x", Text.FullyQualifiedName)));
    }

    [Theory]
    [InlineData("FF", false)]
    [InlineData("FT", true)]
    [InlineData("FU", null)]
    [InlineData("UF", null)]
    [InlineData("FFT", true)]
    public void ADisjunction_TakesItsFirstOperandThatIsNotFalse(string operands, bool? expected)
    {
        var target = Refined(Text, new DisjunctionPattern(null!, Text, Operands(operands)));

        Assert.Equal(expected, LiteralPredicates.Decide(target, new LiteralValue("x", Text.FullyQualifiedName)));
    }

    [Theory]
    [InlineData('T', false)]
    [InlineData('F', true)]
    [InlineData('U', null)]
    [InlineData('A', false)]
    public void ANegation_InvertsOnlyADecision(char operand, bool? expected)
    {
        var target = Refined(Text, new NegatedPattern(null!, Text, Operand(operand)));

        Assert.Equal(expected, LiteralPredicates.Decide(target, new LiteralValue("x", Text.FullyQualifiedName)));
    }

    [Fact]
    public void ACatchAll_IsSatisfiedByAnyValue()
    {
        var target = Refined(Text, Operand('A'));

        Assert.True(LiteralPredicates.Decide(target, new LiteralValue("x", Text.FullyQualifiedName)));
    }

    [Theory]
    [InlineData('T', true)]
    [InlineData('F', false)]
    [InlineData('U', null)]
    [InlineData(null, null)]
    public void ATypeTest_IsDecidedByItsRefinement(char? refinement, bool? expected)
    {
        var target = Refined(Text, new TypeTestPattern(null!, Text, null, refinement: refinement is { } code ? Operand(code) : null));

        Assert.Equal(expected, LiteralPredicates.Decide(target, new LiteralValue("x", Text.FullyQualifiedName)));
    }

    [Theory]
    [InlineData("(?mi)abc", "ABC")]
    [InlineData("(?im:abc)", "abc")]
    [InlineData("(?x-i)abc", "abc")]
    public void ARegexWithAnInlineCaseOption_GetsNoDecision(string body, string input)
    {
        Assert.Null(Match(body, input, Regex.InfiniteMatchTimeout));
    }

    [Theory]
    [InlineData("(?sm)a.b", "a\nb", true)]
    [InlineData("(?n)(a)b", "ab", true)]
    [InlineData("(?s)a.b", "A\nb", false)]
    public void ARegexWithOtherInlineOptions_IsDecided(string body, string input, bool expected)
    {
        Assert.Equal(expected, Match(body, input, Regex.InfiniteMatchTimeout));
    }

    [Fact]
    public void ARegexBodyThatDoesNotDecode_GetsNoDecision()
    {
        Assert.Null(Match("a\"b", "a\"b", Regex.InfiniteMatchTimeout));
    }

    [Fact]
    public void ARegexWithAnUnusableBudget_GetsNoDecision()
    {
        Assert.Null(Match("abc", "abc", TimeSpan.Zero));
    }

    private static bool? Match(string body, string input, TimeSpan matchLimit)
    {
        var target = Refined(Text, new RegexPattern(null!, Text, body));
        return LiteralPredicates.Decide(target, new LiteralValue(input, Text.FullyQualifiedName), matchLimit);
    }

    private static Pattern[] Operands(string codes)
    {
        return [.. codes.Select(Operand)];
    }

    private static Pattern Operand(char code)
    {
        return code switch
        {
            'T' => Comparison(Text, ComparisonKind.Equal, "x"),
            'F' => Comparison(Text, ComparisonKind.Equal, "y"),
            'U' => new RegexPattern(null!, Text, "["),
            _ => new CatchAllPattern(null!, Text, null),
        };
    }

    private static ComparisonPattern Comparison(ClassType type, ComparisonKind kind, object bound)
    {
        var boundType = bound switch
        {
            string => Text,
            float => Float,
            decimal => Money,
            _ => Real,
        };
        return new ComparisonPattern(null!, type, kind, new LiteralExpression(null!, bound, boundType));
    }

    private static RefinedType Refined(IMistType baseType, Pattern predicate)
    {
        return new RefinedType(baseType, null!, "Shaped") { Predicate = predicate };
    }

    private static TypeAlias Alias(IMistType? aliased)
    {
        return new TypeAlias("Named", "Probe", null, null, null!) { AliasedType = aliased };
    }
}