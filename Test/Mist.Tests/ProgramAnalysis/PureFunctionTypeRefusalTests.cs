// Tier 2 (Functional): the assignability, cast and declaration-shape rules judge hand-built pure function type fragments, naming the pure type in what they report.

using Mist.Tests.Helpers;

namespace Mist.Tests.ProgramAnalysis;

[Trait("Tier", "2")]
public sealed class PureFunctionTypeRefusalTests
{
    [Fact]
    public void ARefusedConversionToAPureFunctionType_NamesThePureType()
    {
        var error = Assert.Single(new PurityFragments().Analyze("plain-func-to-pure-func"));

        Assert.Equal("MIST_PA_NO_IMPLICIT_CONVERSION", error.Number.Value);
        Assert.Contains("pure func<", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramModelToAnalysis_ACastToAPureFunctionTypeIsRefused()
    {
        var error = Assert.Single(PureFunctionTypeFragments.Analyze("cast-to-a-pure-function-type"));

        Assert.Equal("MIST_PA_CAST_HAS_NO_CONVERSION", error.Number.Value);
        Assert.Contains("pure func<", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgramModelToAnalysis_OverloadsDifferingOnlyInPurityAreDuplicateSignatures()
    {
        var error = Assert.Single(PureFunctionTypeFragments.Analyze("overloads-differing-only-in-purity"));

        Assert.Equal("MIST_PA_DUPLICATE_SIGNATURE", error.Number.Value);
    }
}