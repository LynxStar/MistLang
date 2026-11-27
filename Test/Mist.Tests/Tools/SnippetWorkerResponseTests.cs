using SnippetEmitter;

namespace Mist.Tests.Tools;

[Trait("Tier", "1")]
public sealed class SnippetWorkerResponseTests
{
    [Fact]
    public void AcceptsResultsInAssignedOrder()
    {
        SnippetWorkers.ValidateResponse(["second.mist", "first.mist"],
            [Result("second.mist"), Result("first.mist")], 0);
    }

    [Fact]
    public void RefusesMissingResult()
    {
        _ = Assert.Throws<IOException>(() => SnippetWorkers.ValidateResponse(["second.mist", "first.mist"],
            [Result("second.mist")], 0));
    }

    [Fact]
    public void RefusesSubstitutedPathAtSameCount()
    {
        _ = Assert.Throws<IOException>(() => SnippetWorkers.ValidateResponse(["second.mist", "first.mist"],
            [Result("second.mist"), Result("other.mist")], 0));
    }

    [Fact]
    public void RefusesReorderedResults()
    {
        _ = Assert.Throws<IOException>(() => SnippetWorkers.ValidateResponse(["second.mist", "first.mist"],
            [Result("first.mist"), Result("second.mist")], 0));
    }

    private static SnippetResult Result(string path)
    {
        return new SnippetResult(path, "class Example {}", "", 0, 1, []);
    }
}