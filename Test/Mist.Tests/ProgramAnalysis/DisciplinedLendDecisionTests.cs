using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "1")]
public sealed class DisciplinedLendDecisionTests
{
    private readonly LifetimeFragments f = new();

    [Theory]
    [InlineData("as", OwnershipKind.Frozen, true)]
    [InlineData("cast", OwnershipKind.Frozen, true)]
    [InlineData("implicit", OwnershipKind.Frozen, true)]
    [InlineData("as", OwnershipKind.Borrowed, false)]
    [InlineData("cast", OwnershipKind.Borrowed, false)]
    [InlineData("implicit", OwnershipKind.Borrowed, false)]
    public void ATransparentConversionPreservesLoanRights(string conversion, OwnershipKind ownership, bool refused)
    {
        var lease = f.Local("lease", ownership: ownership);

        Assert.Equal(refused, TierAnalyzer.RefusesLend(Converted(Ref(lease), conversion), ThisTier.Borrowed));
    }

    [Theory]
    [InlineData("switch", OwnershipKind.Frozen, true)]
    [InlineData("coalesce", OwnershipKind.Frozen, true)]
    [InlineData("switch", OwnershipKind.Borrowed, false)]
    [InlineData("coalesce", OwnershipKind.Borrowed, false)]
    public void ASelectionPreservesLoanRights(string selection, OwnershipKind ownership, bool refused)
    {
        var lease = f.Local("lease", ownership: ownership);

        Assert.Equal(refused, TierAnalyzer.RefusesLend(f.Selected(selection, Ref(lease), Ref(lease)), ThisTier.Borrowed));
    }

    [Theory]
    [InlineData(SubstructuralDiscipline.Linear, false)]
    [InlineData(SubstructuralDiscipline.Affine, false)]
    [InlineData(SubstructuralDiscipline.Relevant, true)]
    [InlineData(SubstructuralDiscipline.None, true)]
    public void OnlyAnExclusivePlainLocalCanLendAsAnOwner(SubstructuralDiscipline discipline, bool refused)
    {
        var local = f.Local("value", f.DisciplinedValue("class", discipline));
        Assert.Equal(refused, TierAnalyzer.RefusesLend(Ref(local), ThisTier.Borrowed));
    }

    [Theory]
    [InlineData(OwnershipKind.Frozen, ThisTier.Frozen, false)]
    [InlineData(OwnershipKind.Frozen, ThisTier.Borrowed, true)]
    [InlineData(OwnershipKind.Borrowed, ThisTier.Owned, true)]
    [InlineData(OwnershipKind.Unrestricted, ThisTier.Frozen, true)]
    public void AParameterRetainsItsDeclaredLoanRights(OwnershipKind ownership, ThisTier required, bool refused)
    {
        var parameter = new Parameter("value", f.Token, new ParameterModifier(false, false, ownership));
        var local = new LocalVariable("value", f.Token) { Parameter = parameter, Ownership = ownership };
        Assert.Equal(refused, TierAnalyzer.RefusesLend(Ref(local), required));
    }
}