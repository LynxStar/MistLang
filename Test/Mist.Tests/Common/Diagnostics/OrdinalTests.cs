using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class OrdinalTests
{
    private static readonly MistDiagnostic Probe = DiagnosticFixtures.Descriptor("FIXTURE_ORDINAL", "probe {0}");

    [Fact]
    public void AdmittedOccurrencesReceiveStrictlyIncreasingOrdinalsAcrossTheOperation()
    {
        var operation = new DiagnosticOperation();
        var first = new DiagnosticBag(operation, PipelineStage.Lexing);
        var second = new DiagnosticBag(operation, PipelineStage.Parsing);

        Report(first, "a");
        Report(second, "b");
        Report(first, "c");

        Assert.Equal([1, 3], first.Admitted.Select(occurrence => occurrence.Ordinal));
        Assert.Equal([2], second.Admitted.Select(occurrence => occurrence.Ordinal));
        Assert.Equal(3, operation.Admitted);
    }

    [Fact]
    public void ABagWithoutAnOperationOwnsOneOfItsOwn()
    {
        var one = new DiagnosticBag();
        var two = new DiagnosticBag();

        Report(one, "a");
        Report(two, "b");

        Assert.Equal(1, one.Admitted[0].Ordinal);
        Assert.Equal(1, two.Admitted[0].Ordinal);
        Assert.NotSame(one.Operation, two.Operation);
    }

    [Fact]
    public void CommitAppendsToTheParentWithOrdinalsIntactAndInAppendOrder()
    {
        var bag = new DiagnosticBag();
        Report(bag, "before");

        var withheld = bag.Withhold(() =>
        {
            Report(bag, "first");
            Report(bag, "second");
        });

        _ = Assert.Single(bag.Admitted);
        Assert.Equal([2, 3], withheld.Occurrences.Select(occurrence => occurrence.Ordinal));

        Report(bag, "between");
        withheld.Commit();

        Assert.Equal(["before", "between", "first", "second"], bag.Admitted.Select(occurrence => occurrence.Message.Replace("probe ", "")));
        Assert.Equal([1, 4, 2, 3], bag.Admitted.Select(occurrence => occurrence.Ordinal));
    }

    [Fact]
    public void DiscardDropsTheWithheldOccurrencesAndConsumesTheirOrdinals()
    {
        var bag = new DiagnosticBag();

        var withheld = bag.Withhold(() => Report(bag, "dropped"));
        withheld.Discard();
        Report(bag, "kept");

        var kept = Assert.Single(bag.Admitted);
        Assert.Equal(2, kept.Ordinal);
    }

    [Fact]
    public void ReplayCommitsAnOccurrenceWithheldEarlierWithItsOriginalOrdinal()
    {
        var bag = new DiagnosticBag();
        var earlier = bag.Withhold(() => Report(bag, "deferred")).Occurrences.Single();
        Report(bag, "later");

        var replay = bag.Withhold(() => Report(bag, "trial"));
        replay.Replay(earlier);
        replay.Discard();

        Assert.Equal([2, 1], bag.Admitted.Select(occurrence => occurrence.Ordinal));
        Assert.Same(earlier, bag.Admitted[1]);
    }

    [Fact]
    public void WithholdsNest()
    {
        var bag = new DiagnosticBag();

        var outer = bag.Withhold(() =>
        {
            Report(bag, "outer");
            var inner = bag.Withhold(() => Report(bag, "inner"));
            Assert.Equal([2], inner.Occurrences.Select(occurrence => occurrence.Ordinal));
            inner.Commit();
        });

        Assert.Empty(bag.Admitted);
        Assert.Equal([1, 2], outer.Occurrences.Select(occurrence => occurrence.Ordinal));
        outer.Commit();
        Assert.Equal([1, 2], bag.Admitted.Select(occurrence => occurrence.Ordinal));
    }

    [Fact]
    public void AnExceptionInsideAWithholdRestoresTheParent()
    {
        var bag = new DiagnosticBag();
        Report(bag, "before");

        _ = Assert.Throws<InvalidOperationException>(() => bag.Withhold(() =>
        {
            Report(bag, "lost");
            throw new InvalidOperationException("trial failed");
        }));

        Report(bag, "after");
        Assert.Equal(["before", "after"], bag.Admitted.Select(occurrence => occurrence.Message.Replace("probe ", "")));
        Assert.Equal([1, 3], bag.Admitted.Select(occurrence => occurrence.Ordinal));
    }

    [Fact]
    public void AForwardedOccurrenceKeepsTheOrdinalItWasAdmittedWith()
    {
        var operation = new DiagnosticOperation();
        var stage = new DiagnosticBag(operation);
        var joined = new DiagnosticBag(operation);
        Report(joined, "first");
        Report(stage, "second");

        joined.Add(stage.Admitted[0]);

        Assert.Equal([1, 2], joined.Admitted.Select(occurrence => occurrence.Ordinal));
        Assert.Equal(2, operation.Admitted);
    }

    [Fact]
    public void AWrapperReferencesItsCauseByOrdinalAndNeverQuotesIt()
    {
        var bag = new DiagnosticBag();
        Report(bag, "cause");
        var cause = bag.Admitted[0];

        bag.Admit(DiagnosticFactory.Create(Probe, cause.Location, PipelineStage.Analysis, [new TextValue("wrapper")], cause: cause.Ordinal));

        var wrapper = bag.Admitted[1];
        Assert.Equal(cause.Ordinal, wrapper.Cause);
        Assert.DoesNotContain(cause.Message, wrapper.Message);
    }

    [Fact]
    public void EqualValuesStayDistinctOccurrences()
    {
        var bag = new DiagnosticBag();
        Report(bag, "same");
        Report(bag, "same");

        Assert.Equal(2, bag.Admitted.Count);
        Assert.NotEqual(bag.Admitted[0], bag.Admitted[1]);
        Assert.Null(typeof(DiagnosticBag).GetMethod("Contains"));
    }

    private static void Report(DiagnosticBag bag, string text)
    {
        bag.ReportAt(Probe, new Absent(AbsenceReason.Unavailable), text);
    }
}