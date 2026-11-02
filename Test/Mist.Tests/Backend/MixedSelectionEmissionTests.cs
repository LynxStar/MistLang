using Mist.Tests.Helpers;
using static Mist.Tests.Helpers.LifetimeFragments;

namespace Mist.Tests.Backend;

[Trait("Tier", "2")]
public sealed class MixedSelectionEmissionTests
{
    [Fact]
    public void SelectedInstallationClearsOnlyTheChosenOwnersFlag()
    {
        var f = new LifetimeFragments();
        var r = f.Local("r");
        var s = f.Local("s");
        var body = HandBuiltModel.Block(Declare(r, f.New(1)), Declare(s, f.New(2)),
            With(f.Selected("switch", Ref(r), Ref(s)), HandBuiltModel.Block()));
        var emitted = Emitted(body);
        Assert.Contains("MistOwnership.Handed(r, ref ", emitted, StringComparison.Ordinal);
        Assert.Contains("MistOwnership.Handed(s, ref ", emitted, StringComparison.Ordinal);
        Assert.Contains("r.Drop();", emitted, StringComparison.Ordinal);
        Assert.Contains("s.Drop();", emitted, StringComparison.Ordinal);
        Assert.DoesNotContain("MistOwnership.Handed((flag switch", emitted, StringComparison.Ordinal);
    }
}