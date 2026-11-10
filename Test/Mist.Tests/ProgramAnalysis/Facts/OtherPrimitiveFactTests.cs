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
    [InlineData("Decimal", "negate", false, false)]
    [InlineData("Decimal", "add", true, true)]
    [InlineData("Decimal", "add", false, false)]
    [InlineData("Decimal", "subtract", true, true)]
    [InlineData("Decimal", "subtract", false, false)]
    [InlineData("Decimal", "multiply-3", true, true)]
    [InlineData("Decimal", "multiply-3", false, false)]
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