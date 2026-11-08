using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Rules;
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
        var bound = HandBuiltRefinements.Read(new ProgramModel.Model.LocalVariable("available", h.Int) { Parameter = available });
        var target = h.Refine(h.Int, ComparisonKind.LessThanOrEqual, bound);
        var used = new Value(h.Int, "used", HandBuiltModel.Node());
        var capacity = new Value(h.Int, "capacity", HandBuiltModel.Node());

        var denial = ContractReader.Denial(target, new Term(used), symbol => ReferenceEquals(symbol.Named, available) ? new Term(capacity).Scale(2) : null);

        var broken = Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<Alternative>>(denial));
        Assert.Contains(new Fact(new Term(capacity).Scale(2).Subtract(new Term(used)).Add(new Term(1))), broken.Facts);
        Assert.Null(ContractReader.Denial(target, new Term(used), _ => null));
    }
}