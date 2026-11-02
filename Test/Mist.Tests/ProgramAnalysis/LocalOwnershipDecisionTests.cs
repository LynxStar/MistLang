using Mist.ProgramAnalysis;
using Mist.ProgramModel.Model.Enums;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class LocalOwnershipDecisionTests
{
    [Theory]
    [InlineData(ThisTier.None, ThisTier.Borrowed, true, false, false, false, ThisTier.None)]
    [InlineData(ThisTier.Frozen, ThisTier.Borrowed, true, false, false, false, ThisTier.Frozen)]
    [InlineData(ThisTier.ReadonlyRef, ThisTier.Owned, true, false, false, true, ThisTier.ReadonlyRef)]
    [InlineData(ThisTier.Owned, ThisTier.Borrowed, false, false, false, false, ThisTier.Owned)]
    [InlineData(ThisTier.Owned, ThisTier.Borrowed, true, true, false, false, ThisTier.Borrowed)]
    [InlineData(ThisTier.Owned, ThisTier.Frozen, true, true, true, false, ThisTier.Frozen)]
    [InlineData(ThisTier.Owned, ThisTier.Owned, true, true, false, false, ThisTier.Owned)]
    [InlineData(ThisTier.Owned, ThisTier.None, true, false, false, false, ThisTier.None)]
    [InlineData(ThisTier.Owned, ThisTier.None, true, false, false, true, ThisTier.Owned)]
    public void CandidateRespectsProvenanceUsesAndDiscipline(ThisTier source, ThisTier required, bool forwards,
        bool later, bool overlapping, bool exclusive, ThisTier expected)
    {
        Assert.Equal(expected, LocalOwnership.Choose(source, required, forwards, later, overlapping, exclusive));
    }
}