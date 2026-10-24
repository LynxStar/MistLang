using Mist.CLI.Steering;

namespace Mist.CLI.Tests.Steering;

[Trait("Tier", "1")]
public sealed class ManagedBlockTests
{
    private const string Body = "Read `.mist/agents/mist-language.md`.";

    private static string Block(string newline = "\n")
    {
        return ManagedBlock.BeginMarker + newline + Body + newline + ManagedBlock.EndMarker;
    }

    [Fact]
    public void Merge_NoFile_CreatesTheBlockAlone()
    {
        var merge = ManagedBlock.Merge(null, Body);

        Assert.Equal(ManagedBlockOutcome.Created, merge.Outcome);
        Assert.Equal(Block() + "\n", merge.Text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    public void Merge_BlankFile_HoldsOnlyTheBlock(string existing)
    {
        var merge = ManagedBlock.Merge(existing, Body);

        Assert.Equal(ManagedBlockOutcome.Appended, merge.Outcome);
        Assert.Equal(Block() + "\n", merge.Text);
    }

    [Fact]
    public void Merge_UserTextWithoutBlock_AppendsAfterABlankLine()
    {
        var merge = ManagedBlock.Merge("# My rules\n\nBe kind.\n\n\n", Body);

        Assert.Equal(ManagedBlockOutcome.Appended, merge.Outcome);
        Assert.Equal("# My rules\n\nBe kind.\n\n" + Block() + "\n", merge.Text);
    }

    [Fact]
    public void Merge_StaleBlock_ReplacesOnlyBetweenTheMarkers()
    {
        var existing = "before\n" + ManagedBlock.BeginMarker + "\nold line\nolder line\n" + ManagedBlock.EndMarker + "\nafter\n";

        var merge = ManagedBlock.Merge(existing, Body);

        Assert.Equal(ManagedBlockOutcome.Replaced, merge.Outcome);
        Assert.Equal("before\n" + Block() + "\nafter\n", merge.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("# My rules\n")]
    [InlineData("before\n<!-- BEGIN MIST-MANAGED -->\nold\n<!-- END MIST-MANAGED -->\nafter")]
    public void Merge_OfItsOwnOutput_IsUnchanged(string? existing)
    {
        var first = ManagedBlock.Merge(existing, Body);

        var second = ManagedBlock.Merge(first.Text, Body);

        Assert.Equal(ManagedBlockOutcome.Unchanged, second.Outcome);
        Assert.Equal(first.Text, second.Text);
    }

    [Fact]
    public void Merge_CrlfFile_WritesTheBlockWithCrlf()
    {
        var merge = ManagedBlock.Merge("# My rules\r\n", "line one\nline two");

        Assert.Equal(
            "# My rules\r\n\r\n" + ManagedBlock.BeginMarker + "\r\nline one\r\nline two\r\n" + ManagedBlock.EndMarker + "\r\n",
            merge.Text);
    }

    [Theory]
    [InlineData("<!-- BEGIN MIST-MANAGED -->\nbody\n")]
    [InlineData("body\n<!-- END MIST-MANAGED -->\n")]
    [InlineData("<!-- END MIST-MANAGED -->\nbody\n<!-- BEGIN MIST-MANAGED -->\n")]
    [InlineData("<!-- BEGIN MIST-MANAGED -->\n<!-- END MIST-MANAGED -->\n<!-- BEGIN MIST-MANAGED -->\n<!-- END MIST-MANAGED -->\n")]
    public void Merge_BrokenFence_IsRefusedAndLeavesTheTextAlone(string existing)
    {
        var merge = ManagedBlock.Merge(existing, Body);

        Assert.Equal(ManagedBlockOutcome.Malformed, merge.Outcome);
        Assert.Equal(existing, merge.Text);
        Assert.NotNull(merge.Error);
    }

    [Theory]
    [InlineData("@AGENTS.md", true)]
    [InlineData("# Rules\n\n@./AGENTS.md\n", true)]
    [InlineData("Follow @AGENTS.md as well.", true)]
    [InlineData("Follow AGENTS.md as well.", false)]
    [InlineData("@docs/AGENTS.md", false)]
    [InlineData("@AGENTS.md.bak", false)]
    public void ImportsAgentsMd_RecognisesOnlyAnImportOfTheRootFile(string text, bool expected)
    {
        Assert.Equal(expected, ManagedBlock.ImportsAgentsMd(text));
    }
}