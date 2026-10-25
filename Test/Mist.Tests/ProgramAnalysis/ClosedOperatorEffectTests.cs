using Mist.ProgramAnalysis.Rules;
using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class ClosedOperatorEffectTests
{
    private readonly FormattingFragments f = new();

    [Theory]
    [InlineData(true, false, 1)]
    [InlineData(true, true, 1)]
    [InlineData(false, false, 0)]
    [InlineData(false, true, 0)]
    public void AReadonlyGenericWrapperApplyingAStorageOperator_FollowsItsSelectedBodyInEitherOrder(bool writes, bool inherited, int findings)
    {
        var (cell, plus) = f.StorageOperator("Cell", writes);
        var operandDefinition = inherited ? f.GenericDerived("Derived", cell).Derived : cell;
        var wrap = f.Wraps("Wrap", plus, operandDefinition, cell, offset: 60);

        var forward = f.Analyze(wrap, plus);
        var backward = f.Analyze(plus, wrap);

        Assert.Equal(findings, forward.Count);
        Assert.Equal(
            forward.Select(finding => (finding.Number.Value, finding.Location.Start.Column)),
            backward.Select(finding => (finding.Number.Value, finding.Location.Start.Column)));
        Assert.All(forward, finding => Assert.Equal(61, finding.Location.Start.Column));
    }

    [Fact]
    public void IndependentOperatorsSharingASymbol_AreJudgedByTheirOwnBodies()
    {
        var (writer, writes) = f.StorageOperator("Writer", writes: true);
        var (reader, reads) = f.StorageOperator("Reader", writes: false);

        var finding = Assert.Single(f.Analyze(
            f.Wraps("Read", reads, reader, reader, offset: 40), writes, reads, f.Wraps("Write", writes, writer, writer, offset: 60)));

        Assert.Equal(61, finding.Location.Start.Column);
    }

    [Theory]
    [InlineData(true, false, 0)]
    [InlineData(false, false, 1)]
    [InlineData(true, true, 0)]
    [InlineData(false, true, 1)]
    public void APureGenericWrapper_KeepsThePromiseItsSelectedOperatorDeclares(bool declaredPure, bool inherited, int findings)
    {
        var (cell, plus) = f.StorageOperator("Cell", writes: false, declaredPure);
        var operandDefinition = inherited ? f.GenericDerived("Derived", cell).Derived : cell;
        var wrap = f.Wraps("Wrap", plus, operandDefinition, cell, offset: 60, isPure: true);

        var found = f.Model.Analyze(new PurityAnalyzer(), wrap.Body, owner: wrap.DeclaringType, member: wrap);

        Assert.Equal(findings, found.Count);
        Assert.All(found, finding => Assert.Equal(61, finding.Location.Start.Column));
    }
}