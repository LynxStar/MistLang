using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class ContractReaderTests
{
    [Fact]
    public void TheDenialOfSeparatedIntervalsIncludesTheHoleAndBothOutsideEnds()
    {
        var h = new HandBuiltRefinements();
        var target = HandBuiltRefinements.RefinePattern(h.Int, new DisjunctionPattern(HandBuiltModel.Node(), h.Int,
            [h.Compare(h.Int, ComparisonKind.Equal, 1L), h.Compare(h.Int, ComparisonKind.Equal, 100L)]));
        var value = new Value(h.Int, "value", HandBuiltModel.Node());

        var denial = ContractReader.Denial(target, new Term(value), _ => null);

        Assert.NotNull(denial);
        Assert.Equal(3, denial.Count);
        Assert.Equal(new Alternative([new Fact(new Term(value))]), denial[0]);
        Assert.Equal(new Alternative([new Fact(new Term(value).Scale(-1).Add(new Term(2))), new Fact(new Term(value).Subtract(new Term(99)))]), denial[1]);
        Assert.Equal(new Alternative([new Fact(new Term(value).Scale(-1).Add(new Term(101)))]), denial[2]);
    }

    [Fact]
    public void ASymbolicContractIsDeniedAgainstTheResolvedArgumentTerm()
    {
        var h = new HandBuiltRefinements();
        var available = new Parameter("available", h.Int);
        var bound = HandBuiltRefinements.Read(new LocalVariable("available", h.Int) { Parameter = available });
        var target = h.Refine(h.Int, ComparisonKind.LessThanOrEqual, bound);
        var used = new Value(h.Int, "used", HandBuiltModel.Node());
        var capacity = new Value(h.Int, "capacity", HandBuiltModel.Node());

        var denial = ContractReader.Denial(target, new Term(used), symbol => ReferenceEquals(symbol.Named, available) ? new Term(capacity).Scale(2) : null);

        var broken = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<Alternative>>(denial));
        Assert.Contains(new Fact(new Term(capacity).Scale(2).Subtract(new Term(used)).Add(new Term(1))), broken.Facts);
        Assert.Null(ContractReader.Denial(target, new Term(used), _ => null));
    }

    [Fact]
    public void AnExpressionOperandIsDeniedAsOneFactOverThreeValues()
    {
        var h = new HandBuiltRefinements();
        var show = new ShowContract(h);
        var count = new Value(h.Int, "count", HandBuiltModel.Node());
        var length = new Value(h.Int, "items.Length", HandBuiltModel.Node());
        var start = new Value(h.Int, "start", HandBuiltModel.Node());
        var required = Assert.IsType<Requirement>(Requirement.OfType(show.CountType));
        var bound = Assert.Single(required.Bounds);

        var denial = ContractReader.Denial(required, new Term(count), symbol => symbol switch
        {
            { Operand: MemberAccessExpression, Named: var named } when ReferenceEquals(named, show.Items) => new Term(length),
            { Named: var named } when ReferenceEquals(named, show.Start) => new Term(start),
            _ => null,
        });

        _ = Assert.IsType<BinaryExpression>(bound.Operand);
        Assert.Equal(new Term(length).Subtract(new Term(start)), ContractReader.Limit(bound, symbol => symbol.Operand is null ? new Term(start) : new Term(length)));
        var alternatives = Assert.IsAssignableFrom<IReadOnlyList<Alternative>>(denial);
        Assert.Equal(2, alternatives.Count);
        Assert.Equal(new Alternative([new Fact(new Term(count).Add(new Term(1)))]), alternatives[0]);
        var fact = Assert.Single(alternatives[1].Facts);
        Assert.Equal(new Fact(new Term(count).Scale(-1).Add(new Term(length)).Subtract(new Term(start)).Add(new Term(1))), fact);
        Assert.Equal(3, fact.Term.Variables.Length);
        Assert.Null(ContractReader.Denial(required, new Term(count), symbol => symbol.Operand is null ? new Term(start) : null));
    }

    [Fact]
    public void AWhereClauseOverAnExpressionBecomesAFactAtEntry()
    {
        var h = new HandBuiltRefinements();
        var show = new ShowContract(h);
        var reading = new FactReading(new LiftedReturns());
        var state = FactState.Empty;
        var items = new LocalVariable("items", show.Span) { Parameter = show.Items };
        var startLocal = new LocalVariable("start", show.ZeroOrPositive) { Parameter = show.Start };

        var count = reading.Read(ref state, HandBuiltRefinements.Read(new LocalVariable("count", show.CountType) { Parameter = show.Count }));
        var start = reading.Read(ref state, HandBuiltRefinements.Read(startLocal));
        var length = reading.Read(ref state, h.Length(HandBuiltRefinements.Read(items)));

        Assert.Contains(new Fact(count.Subtract(length).Add(start)), state.Bag.Common);
        var guarded = state.Bag.Add(new Fact(count.Scale(-1).Add(new Term(1))));
        var outside = new Alternative([new Fact(length.Subtract(start))]);
        _ = Assert.IsType<Proven>(Chain.Prove(guarded, [outside], start.Variables.Concat(length.Variables).ToHashSet()));
        _ = Assert.IsType<Refuted>(Chain.Prove(state.Bag, [outside], start.Variables.Concat(length.Variables).ToHashSet()));
    }

    private sealed class ShowContract
    {
        public ShowContract(HandBuiltRefinements h)
        {
            Span = h.Sequence("Span");
            ZeroOrPositive = h.Refine(h.Int, ComparisonKind.GreaterThanOrEqual, 0L, "ZeroOrPositiveInt");
            Items = new Parameter("items", Span);
            Start = new Parameter("start", ZeroOrPositive);
            var operand = h.Binary(h.Length(HandBuiltRefinements.Read(new LocalVariable("items", Span) { Parameter = Items })), BinaryOperatorKind.Subtract,
                HandBuiltRefinements.Read(new LocalVariable("start", ZeroOrPositive) { Parameter = Start }), h.Int);
            CountType = h.Refine(ZeroOrPositive, ComparisonKind.LessThanOrEqual, operand, null);
            Count = new Parameter("count", CountType);
        }

        public IMistType Span { get; }
        public IMistType ZeroOrPositive { get; }
        public IMistType CountType { get; }
        public Parameter Items { get; }
        public Parameter Start { get; }
        public Parameter Count { get; }
    }
}