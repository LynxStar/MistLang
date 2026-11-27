using Mist.CLI.Steering;

namespace Mist.CLI.Tests.Steering;

[Trait("Tier", "1")]
public sealed class ManagedBlockLegacyMarkerTests
{
    [Fact]
    public void Merge_LegacyMarkerPair_ReplacesBlockAndPreservesOutsideText()
    {
        const string prefix = "# Project rules\nKeep these instructions.  \n\n";
        const string suffix = "\n\n  Keep this final note.";
        const string body = "Read `.mist/agents/mist-language.md`.";
        var existing = prefix + "<!-- BEGIN MIST-MANAGED -->\nOld instructions.\n<!-- END MIST-MANAGED -->" + suffix;
        var expected = prefix + ManagedBlock.BeginMarker + "\n" + body + "\n" + ManagedBlock.EndMarker + suffix;

        var merge = ManagedBlock.Merge(existing, body);

        Assert.Equal(ManagedBlockOutcome.Replaced, merge.Outcome);
        Assert.Equal(expected, merge.Text);
    }
}