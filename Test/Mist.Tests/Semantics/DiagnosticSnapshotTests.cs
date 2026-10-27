using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;
using Mist.Tests.Helpers;

namespace Mist.Tests.Semantics;

[Trait("Tier", "1")]
public sealed class DiagnosticSnapshotTests
{
    [Fact]
    public void CastTargetsWithEqualShortNamesKeepDistinctSymbolIdentities()
    {
        var owner = HandBuiltModel.Class("Source");
        var first = new ClassType("Result", "First", new Scope(null), null, null);
        var second = new ClassType("Result", "Second", new Scope(null), null, null);
        var firstCast = HandBuiltModel.Conversion(owner, owner, first);
        var secondCast = HandBuiltModel.Conversion(owner, owner, second);

        var firstSnapshot = DiagnosticSnapshot.Symbol(firstCast, "cast to Result");
        var secondSnapshot = DiagnosticSnapshot.Symbol(secondCast, "cast to Result");

        Assert.Equal(firstSnapshot.Spelling, secondSnapshot.Spelling);
        Assert.NotEqual(firstSnapshot.Identity, secondSnapshot.Identity);
        Assert.Contains("First.Result", firstSnapshot.Identity, StringComparison.Ordinal);
        Assert.Contains("Second.Result", secondSnapshot.Identity, StringComparison.Ordinal);
    }
}