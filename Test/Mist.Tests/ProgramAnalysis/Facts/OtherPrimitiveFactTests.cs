using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;
using static Mist.Tests.ProgramAnalysis.Facts.FactModel;
using Comparison = Mist.ProgramAnalysis.Facts.Fact;

namespace Mist.Tests.ProgramAnalysis.Facts;

[Trait("Tier", "1")]
public sealed class OtherPrimitiveFactTests
{
    [Fact]
    public void ARealFactKeepsItsRelationAndConstantUnrounded()
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());

        var strict = new Comparison(Term(-1, (x, 2)), Relation.LessThan);
        var loose = Le(-1, (x, 2));
        var reduced = Le(-4, (x, 6));

        Assert.Equal(Relation.LessThan, strict.Relation);
        Assert.Equal(Term(-1, (x, 2)), strict.Term);
        Assert.Equal(Term(-1, (x, 2)), loose.Term);
        Assert.Equal(Term(-2, (x, 3)), reduced.Term);
        Assert.False(Eq(-1, (x, 2)).IsContradiction);
    }

    [Theory]
    [InlineData("Double")]
    [InlineData("Int32")]
    public void AStrictRealFactIsNotRespelledWithMinusOne(string type)
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Built.Type(type), "x", HandBuiltModel.Node());
        var below = new Comparison(Term(-1, (x, 1)), Relation.LessThan);
        var positive = new Comparison(Term(0, (x, -1)), Relation.LessThan);

        var proof = Chain.Prove(MakeBag(below), [new Alternative([positive])], new HashSet<Value> { x }, Limit);

        if(type == "Int32")
        {
            _ = Assert.IsType<Proven>(proof);
        }
        else
        {
            var refuted = Assert.IsType<Refuted>(proof);
            Assert.Contains(below, refuted.FailingCase.Facts);
            Assert.Contains(positive, refuted.FailingCase.Facts);
            Assert.All(refuted.FailingCase.Facts, fact => Assert.Equal(Relation.LessThan, fact.Relation));
        }
    }

    [Fact]
    public void AStrictRealFactIsSpelledWithLessThan()
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());
        var y = new Value(h.Double, "y", HandBuiltModel.Node());

        Assert.Equal(["x < y"], FailingCase.Spelled([new Comparison(Term(0, (x, 1), (y, -1)), Relation.LessThan)]));
        Assert.Equal(["x > y"], FailingCase.Spelled([new Comparison(Term(0, (x, -1), (y, 1)), Relation.LessThan)]));
    }

    [Theory]
    [InlineData("Double", 0, 1, "LessThan", "x < 0")]
    [InlineData("Double", -5, 2, "LessThanOrEqual", "x <= 2.5")]
    [InlineData("Double", 5, -2, "LessThan", "x > 2.5")]
    [InlineData("Decimal", -1, 3, "LessThan", "x < 1 / 3")]
    [InlineData("Decimal", -7, 4, "Equal", "x == 1.75")]
    public void ASingleValueRealFactIsSpelledWithoutRounding(string type, int constant, int multiplier, string relation, string expected)
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Built.Type(type), "x", HandBuiltModel.Node());
        var fact = new Comparison(Term(constant, (x, multiplier)), Enum.Parse<Relation>(relation));

        Assert.Equal([expected], FailingCase.Spelled([fact]));
        Assert.Equal($"fails when {expected}", FailingCase.Describe(new Refuted(new Alternative([fact]), false)));
    }

    [Fact]
    public void ABinaryBoundNextToARoundNumberIsSpelledAgainstThatNumber()
    {
        var x = new Value(new HandBuiltRefinements().Double, "x", HandBuiltModel.Node());
        var belowZero = RealFacts.Compare(new Term(x), ComparisonKind.LessThanOrEqual, RealNumbers.Of(-double.Epsilon)!.Value);
        var aboveOne = RealFacts.Compare(new Term(x), ComparisonKind.GreaterThanOrEqual, RealNumbers.Of(double.BitIncrement(1.0))!.Value);

        Assert.Equal(["x < 0", "x > 1"], FailingCase.Spelled([belowZero, aboveOne]));
    }

    [Fact]
    public void ARealParametersOwnRefinementIsNotTheFailingCase()
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Refine(h.Double, ComparisonKind.GreaterThan, -1.0), "x", HandBuiltModel.Node());
        var declared = Assert.Single(Assert.Single(RealFacts.Within(RealRequirement.OfType(x.Type)!, new Term(x))!).Facts);
        var denied = RealFacts.Compare(new Term(x), ComparisonKind.LessThan, new Term(0));

        Assert.Equal("fails when x < 0", FailingCase.Describe(new Refuted(new Alternative([declared, denied]), false)));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AnOrderAnEqualityAlreadyStatesIsNotSpelledAgain(bool below)
    {
        var kind = below ? ComparisonKind.LessThanOrEqual : ComparisonKind.GreaterThanOrEqual;
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());
        var y = new Value(h.Double, "y", HandBuiltModel.Node());
        var equal = RealFacts.Compare(new Term(x), ComparisonKind.Equal, new Term(y));

        Assert.Equal(["x == y"], FailingCase.Spelled([equal, RealFacts.Compare(new Term(x), kind, new Term(y))]));
        Assert.Equal(["x == y", "x < y"], FailingCase.Spelled([equal, RealFacts.Compare(new Term(x), ComparisonKind.LessThan, new Term(y))]));
    }

    [Fact]
    public void ARealFactsDenialKeepsTheBoundaryOnTheOtherSide()
    {
        var x = new Value(new HandBuiltRefinements().Double, "x", HandBuiltModel.Node());
        var atMost = Le(-1, (x, 1));

        var denial = Assert.Single(atMost.Denial());

        Assert.Equal(new Comparison(Term(1, (x, -1)), Relation.LessThan), Assert.Single(denial.Facts));
    }

    [Theory]
    [InlineData("Double", "negate", true, true)]
    [InlineData("Double", "negate", false, true)]
    [InlineData("Double", "multiply-2", true, true)]
    [InlineData("Double", "multiply-2", false, false)]
    [InlineData("Double", "multiply-3", true, false)]
    [InlineData("Double", "divide-2", true, true)]
    [InlineData("Double", "divide-2", false, false)]
    [InlineData("Double", "add", true, false)]
    [InlineData("Single", "multiply-2", true, true)]
    [InlineData("Single", "multiply-2", false, false)]
    [InlineData("Decimal", "negate", true, true)]
    [InlineData("Decimal", "negate", false, true)]
    [InlineData("Decimal", "add", true, false)]
    [InlineData("Decimal", "add", false, false)]
    [InlineData("Decimal", "subtract", true, false)]
    [InlineData("Decimal", "subtract", false, false)]
    [InlineData("Decimal", "multiply-3", true, false)]
    [InlineData("Decimal", "multiply-3", false, false)]
    [InlineData("Decimal", "multiply-2", true, false)]
    [InlineData("Decimal", "divide-2", true, false)]
    public void EachRealOperationIsExactOnlyAsTheDesignLists(string type, string operation, bool bounded, bool exact)
    {
        var h = new HandBuiltRefinements();
        var real = h.Built.Type(type);
        var x = new Value(real, "x", HandBuiltModel.Node());
        var local = HandBuiltRefinements.Local(real);
        object number(int value)
        {
            return type switch { "Single" => (float)value, "Decimal" => (decimal)value, _ => (double)value };
        }

        var operand = new Term(x);
        var (expression, term) = operation switch
        {
            "negate" => (new UnaryExpression(HandBuiltModel.Node(), UnaryOperatorKind.Negate, local, real), operand.Scale(-1)),
            "add" => (h.Binary(local, BinaryOperatorKind.Add, h.Literal(number(1), real), real), operand.Add(new Term(1))),
            "subtract" => (h.Binary(local, BinaryOperatorKind.Subtract, h.Literal(number(1), real), real), operand.Add(new Term(-1))),
            "multiply-2" => (h.Binary(local, BinaryOperatorKind.Multiply, h.Literal(number(2), real), real), operand.Scale(2)),
            "multiply-3" => (h.Binary(local, BinaryOperatorKind.Multiply, h.Literal(number(3), real), real), operand.Scale(3)),
            _ => (h.Binary(local, BinaryOperatorKind.Divide, h.Literal(number(2), real), real), operand),
        };
        var bag = MakeBag();
        if(bounded)
        {
            bag = bag.Add(RealFacts.Compare(operand, ComparisonKind.GreaterThanOrEqual, new Term(1)))
                .Add(RealFacts.Compare(operand, ComparisonKind.LessThanOrEqual, new Term(1000)));
        }

        Assert.Equal(exact, Exactness.Holds(expression, term, bag, Limit));
    }

    [Theory]
    [InlineData("add")]
    [InlineData("subtract")]
    [InlineData("multiply")]
    public void ADecimalResultProvenWithinRangeCanStillRoundSoItIsNotExact(string operation)
    {
        var h = new HandBuiltRefinements();
        var real = h.Built.Type("Decimal");
        var x = new Value(real, "x", HandBuiltModel.Node());
        var local = HandBuiltRefinements.Local(real);
        var operand = new Term(x);
        var tiny = h.Literal(0.0000000000000000000000000001m, real);
        var (expression, term) = operation switch
        {
            "add" => (h.Binary(local, BinaryOperatorKind.Add, tiny, real), operand),
            "subtract" => (h.Binary(local, BinaryOperatorKind.Subtract, tiny, real), operand),
            _ => (h.Binary(local, BinaryOperatorKind.Multiply, h.Literal(3m, real), real), operand.Scale(3)),
        };
        var bag = MakeBag(RealFacts.Compare(operand, ComparisonKind.GreaterThanOrEqual, new Term(1)),
            RealFacts.Compare(operand, ComparisonKind.LessThanOrEqual, new Term(10)));

        Assert.False(Exactness.Holds(expression, term, bag, Limit));
    }

    [Fact]
    public void HalvingADoubleThatMightBeTinyIsNotExact()
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());
        var halve = h.Binary(HandBuiltRefinements.Local(h.Double), BinaryOperatorKind.Divide, h.Literal(2.0), h.Double);
        var operand = new Term(x);
        var bag = MakeBag(RealFacts.Compare(operand, ComparisonKind.GreaterThan, new Term(0)),
            RealFacts.Compare(operand, ComparisonKind.LessThanOrEqual, new Term(1000)));

        Assert.False(Exactness.Holds(halve, operand, bag, Limit));
        Assert.True(Exactness.Holds(halve, operand, bag.Add(RealFacts.Compare(operand, ComparisonKind.GreaterThanOrEqual, new Rational(1, 1 << 20))), Limit));
    }

    [Theory]
    [InlineData("abc", true)]
    [InlineData("ABC", false)]
    public void AMatchIsProvenFromALiteral(string text, bool proven)
    {
        var h = new HandBuiltRefinements();
        var target = h.Regex("^[a-z]+$");
        var s = new Value(h.String, "s", HandBuiltModel.Node());

        Assert.Equal(proven, Matched(MakeBag(), s, target, literal: (s, text)));
    }

    [Theory]
    [InlineData("^[a-z]+$", true)]
    [InlineData("^[a-z]*$", false)]
    public void AMatchIsProvenFromTheSameMatch(string known, bool proven)
    {
        var h = new HandBuiltRefinements();
        var s = new Value(h.String, "s", HandBuiltModel.Node());
        var typed = new Value(h.Regex(known), "typed", HandBuiltModel.Node());

        Assert.Equal(proven, Matched(MakeBag(), s, h.Regex("^[a-z]+$"), matched: (s, known)));
        Assert.Equal(proven, Matched(MakeBag(), typed, h.Regex("^[a-z]+$")));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AMatchIsProvenThroughAnEqualString(bool equal)
    {
        var h = new HandBuiltRefinements();
        var s = new Value(h.String, "s", HandBuiltModel.Node());
        var t = new Value(h.String, "t", HandBuiltModel.Node());
        var target = h.Regex("^[a-z]+$");
        var bag = equal ? MakeBag(Eq(0, (s, 1), (t, -1))) : MakeBag();

        Assert.Equal(equal, Matched(bag, s, target, matched: (t, "^[a-z]+$")));
        Assert.Equal(equal, Matched(bag, s, target, literal: (t, "abc")));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void AMatchFollowsTheLiteralOfTheSelectedAlternative(int? flag, bool proven)
    {
        var h = new HandBuiltRefinements();
        var s = new Value(h.String, "s", HandBuiltModel.Node());
        var letters = new Value(h.String, "letters", HandBuiltModel.Node());
        var digits = new Value(h.String, "digits", HandBuiltModel.Node());
        var chosen = new Value(h.Bool, "flag", HandBuiltModel.Node());
        var joined = new Bag([], [new Alternative([Eq(-1, (chosen, 1)), Eq(0, (s, 1), (letters, -1))]), new Alternative([Eq(0, (chosen, 1)), Eq(0, (s, 1), (digits, -1))])]);
        var bag = flag is { } selected ? joined.Add(Eq(-selected, (chosen, 1))) : joined;

        Assert.Equal(proven, Matching.Proven(bag, s, h.Regex("^[a-z]+$"),
            value => value == letters ? new LiteralValue("abc", "Standard.String") : value == digits ? new LiteralValue("123", "Standard.String") : null,
            _ => [], LiteralPredicates.MatchLimit));
    }

    [Fact]
    public void ARealSymbolicBoundKeepsItsRelationOnlyAgainstAValueOfTheSameKind()
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());
        var y = new Value(h.Double, "y", HandBuiltModel.Node());
        var n = new Value(h.Int, "n", HandBuiltModel.Node());

        var below = RealFacts.Bound(RealKind.Double, new Term(x), ComparisonKind.LessThan, new Term(y));

        Assert.Equal(new Comparison(Term(0, (x, 1), (y, -1)), Relation.LessThan), below);
        Assert.Null(RealFacts.Bound(RealKind.Double, new Term(x), ComparisonKind.LessThan, new Term(n)));
        Assert.Null(RealFacts.Bound(RealKind.Double, new Term(x), ComparisonKind.LessThan, new Term(y).Add(new Term(1))));
        Assert.Null(RealFacts.Bound(RealKind.Double, new Term(x), ComparisonKind.LessThan, new Term(y).Scale(2)));
        Assert.Null(RealFacts.Bound(RealKind.Single, new Term(x), ComparisonKind.LessThan, new Term(y)));
    }

    [Fact]
    public void AWholeNumberCutoffOnARealIsReadAsARealSet()
    {
        var h = new HandBuiltRefinements();
        var y = HandBuiltRefinements.Local(h.Double, "y");
        var bounded = HandBuiltRefinements.RefinePattern(h.Double, new ConjunctionPattern(HandBuiltModel.Node(), h.Double,
            [h.Compare(h.Double, ComparisonKind.GreaterThan, 0), h.Compare(h.Double, ComparisonKind.LessThan, y)]));

        Assert.Equal(RealRequirement.OfType(h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0)), RealRequirement.Literal(h.Refine(h.Double, ComparisonKind.GreaterThan, 0)));
        Assert.Equal(RealRequirement.OfType(h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0)), RealRequirement.Literal(bounded));
        Assert.Equal(RealSet.Everything(RealKind.Double), RealRequirement.Literal(h.Refine(h.Double, ComparisonKind.LessThan, y)));
        Assert.Null(RealRequirement.OfType(bounded));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(0.0)]
    public void ARealConjunctionIsReadAsItsLiteralPartAndItsSymbolicBounds(object cutoff)
    {
        var h = new HandBuiltRefinements();
        var y = HandBuiltRefinements.Local(h.Double, "y");
        var bounded = HandBuiltRefinements.RefinePattern(h.Double, new ConjunctionPattern(HandBuiltModel.Node(), h.Double,
            [h.Compare(h.Double, ComparisonKind.GreaterThan, cutoff), h.Compare(h.Double, ComparisonKind.LessThan, y)]));

        var read = Requirement.OfReal(bounded);

        Assert.NotNull(read);
        var bound = Assert.Single(read.Bounds);
        Assert.Equal(ComparisonKind.LessThan, bound.Kind);
        Assert.Same(y.Local, bound.Named);
        Assert.True(read.Values.IsEverything);
        Assert.Equal(RealRequirement.OfType(h.Refine(h.Double, ComparisonKind.GreaterThan, 0.0)), RealRequirement.Literal(bounded));
        Assert.Same(Requirement.Anything, Requirement.OfReal(h.Double));
        Assert.Null(Requirement.OfReal(h.Int));
    }

    [Fact]
    public void AnUnreadableConjunctLeavesTheWholeRealConjunctionUnread()
    {
        var h = new HandBuiltRefinements();
        var y = HandBuiltRefinements.Local(h.Double, "y");
        var either = new DisjunctionPattern(HandBuiltModel.Node(), h.Double,
            [h.Compare(h.Double, ComparisonKind.GreaterThan, 0.0), h.Compare(h.Double, ComparisonKind.LessThan, y)]);
        var mixed = HandBuiltRefinements.RefinePattern(h.Double, new ConjunctionPattern(HandBuiltModel.Node(), h.Double,
            [h.Compare(h.Double, ComparisonKind.GreaterThan, 0.0), either]));

        Assert.Null(Requirement.OfReal(mixed));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyAnEqualityWithItsWitnessOrdersItsValues(bool witnessed)
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());
        var y = new Value(h.Double, "y", HandBuiltModel.Node());
        var equal = RealFacts.Compare(new Term(x), ComparisonKind.Equal, new Term(y));
        var above = new Value(h.Double, "x == y", HandBuiltModel.Node());
        var bag = Bag.Empty.Add(equal);
        bag = witnessed ? RealFacts.Witness(new Term(x), new Term(y), above).Aggregate(bag, (current, fact) => current.Add(fact)) : bag;

        Assert.Equal(witnessed, RealFacts.Ordered(bag, x));
        Assert.Equal(witnessed, RealFacts.Ordered(bag, y));
        _ = Assert.IsType<Proven>(Prove(bag, RealFacts.Compare(new Term(x), ComparisonKind.GreaterThan, new Term(y)), x, y));
        _ = Assert.IsType<Refuted>(Prove(bag, RealFacts.Compare(new Term(x), ComparisonKind.GreaterThanOrEqual, new Term(y)), x, y));
    }

    [Theory]
    [InlineData("x == x")]
    [InlineData("x + y == x")]
    public void AWitnessKeepsTheValuesOfAnEqualityWhoseSidesCancel(string shape)
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());
        var y = new Value(h.Double, "y", HandBuiltModel.Node());
        var above = new Value(h.Double, shape, HandBuiltModel.Node());
        var first = shape == "x == x" ? new Term(x) : new Term(x).Add(new Term(y));
        var equal = RealFacts.Compare(first, ComparisonKind.Equal, new Term(x));
        var witness = RealFacts.Witness(first, new Term(x), above);
        var bag = witness.Aggregate(Bag.Empty.Add(equal), (current, fact) => current.Add(fact));

        Assert.Equal(shape == "x == x" ? 1 : 2, witness.Count);
        Assert.False(RealFacts.Ordered(Bag.Empty.Add(equal), x));
        Assert.True(RealFacts.Ordered(bag, x));
        Assert.Equal(shape != "x == x", RealFacts.Ordered(bag, y));
        Assert.Empty(RealFacts.Witness(new Term(2), new Term(2), above));
        _ = Assert.IsType<Refuted>(Prove(witness.Aggregate(Bag.Empty, (current, fact) => current.Add(fact)),
            RealFacts.Compare(new Term(x), ComparisonKind.GreaterThan, new Term(y)), x, y));
    }

    [Fact]
    public void ABooleanInitializerCarriesTheWitnessOnItsTrueAlternativeOnly()
    {
        var h = new HandBuiltRefinements();
        var x = new Value(h.Double, "x", HandBuiltModel.Node());
        var y = new Value(h.Double, "y", HandBuiltModel.Node());
        var ok = new Value(h.Bool, "ok", HandBuiltModel.Node());
        var equal = RealFacts.Compare(new Term(x), ComparisonKind.Equal, new Term(y));
        var witness = RealFacts.Witness(new Term(x), new Term(y), new Value(h.Double, "x == y", HandBuiltModel.Node()));

        var alternatives = Booleans.Initializer(ok, equal, positive: true, [Alternative.Empty], witness).ToList();

        Assert.Equal(2, alternatives.Count);
        Assert.All(witness, fact => Assert.Contains(fact, alternatives[0].Facts));
        Assert.All(witness, fact => Assert.DoesNotContain(fact, alternatives[1].Facts));
        Assert.True(RealFacts.Ordered(new Bag([], alternatives).Add(Eq(-1, (ok, 1))), x));
        Assert.False(RealFacts.Ordered(new Bag([], alternatives).Add(Eq(0, (ok, 1))), x));
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(0, false)]
    public void ABooleanInitializerKeepsOneAlternativePerOutcome(int flag, bool below)
    {
        var model = new FactModel();
        var i = model.Value("i");
        var n = model.Value("n");
        var ok = new Value(model.Expressions.Bool, "ok", HandBuiltModel.Node());
        var holds = Le(1, (i, 1), (n, -1));
        var bag = new Bag([], Booleans.Initializer(ok, holds, positive: true, holds.Denial()));

        var selected = bag.Add(Eq(-flag, (ok, 1)));

        Assert.Equal(2, bag.Alternatives.Length);
        _ = Assert.IsType<Refuted>(Prove(bag, Le(0, (i, -1), (n, 1)), i, n));
        _ = Assert.IsType<Proven>(Prove(selected, below ? Le(0, (i, -1), (n, 1)) : Le(1, (i, 1), (n, -1)), i, n));
    }

    private static bool Matched(Bag bag, Value text, IMistType target, (Value Value, string Text)? literal = null, (Value Value, string Pattern)? matched = null)
    {
        return Matching.Proven(bag, text, target,
            value => literal is { } known && known.Value == value ? new LiteralValue(known.Text, "Standard.String") : null,
            value => matched is { } match && match.Value == value ? [match.Pattern] : [],
            LiteralPredicates.MatchLimit);
    }
}