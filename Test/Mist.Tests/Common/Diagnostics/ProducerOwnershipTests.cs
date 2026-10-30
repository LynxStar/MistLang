using Mist.Common.Diagnostics;

namespace Mist.Tests.Common.Diagnostics;

[Trait("Tier", "1")]
public sealed class ProducerOwnershipTests
{
    [Fact]
    public void ReplayReusesOnlyAdmittedSourceOwnersAndPreservesDistinctReports()
    {
        var bag = new DiagnosticBag();
        var owner = new object();
        bag.AdmitOwned(owner, Finding());
        bag.AdmitOwned(owner, Finding());
        var sources = bag.ToImmutableArray();
        var discarded = bag.Withhold(() => bag.AdmitOwned(owner, Finding()));
        discarded.Discard();
        Withheld replayed;
        using(bag.ReplayOwnedFailures())
        {
            replayed = bag.Withhold(() =>
            {
                bag.AdmitOwned(owner, Finding());
                bag.AdmitOwned(owner, Finding());
                bag.AdmitOwned(owner, Finding());
            });
        }

        Assert.Equal(sources[0].Ordinal, replayed.Occurrences[0].Ordinal);
        Assert.Equal(sources[1].Ordinal, replayed.Occurrences[1].Ordinal);
        Assert.NotEqual(discarded.Occurrences[0].Ordinal, replayed.Occurrences[2].Ordinal);
        Assert.NotEqual(sources[0].Ordinal, sources[1].Ordinal);
        replayed.Discard();
        Assert.Equal(2, bag.Admitted.Count);
    }

    [Fact]
    public void DeferredReplayRegistersOneSourceOccurrenceForCloneOwnership()
    {
        var bag = new DiagnosticBag();
        var owner = new object();
        var deferred = bag.Withhold(() => bag.AdmitOwned(owner, Finding()));
        var pending = Assert.Single(deferred.Occurrences);
        deferred.Discard();
        bag.Replay(pending, owner);
        Withheld replayed;
        using(bag.ReplayOwnedFailures())
        {
            replayed = bag.Withhold(() =>
            {
                bag.Replay(Finding().Occurrence, owner);
                bag.Replay(Finding().Occurrence, owner);
            });
        }

        Assert.Equal(pending.Ordinal, replayed.Occurrences[0].Ordinal);
        Assert.NotEqual(pending.Ordinal, replayed.Occurrences[1].Ordinal);
        replayed.Discard();
    }

    [Fact]
    public void EqualFindingsFromDifferentOwnersKeepTheirOwnOrdinals()
    {
        var bag = new DiagnosticBag();
        bag.AdmitOwned(new object(), Finding());
        var source = Assert.Single(bag.Admitted);
        Withheld replayed;
        using(bag.ReplayOwnedFailures())
        {
            replayed = bag.Withhold(() => bag.AdmitOwned(new object(), Finding()));
        }

        Assert.NotEqual(source.Ordinal, Assert.Single(replayed.Occurrences).Ordinal);
        replayed.Discard();
    }

    [Fact]
    public void NestedReplayAndExceptionRestoreSourceRegistration()
    {
        var bag = new DiagnosticBag();
        var owner = new object();
        bag.AdmitOwned(owner, Finding());
        using(bag.ReplayOwnedFailures())
        {
            _ = Assert.Throws<InvalidOperationException>(() =>
            {
                using var nested = bag.ReplayOwnedFailures();
                _ = bag.Withhold(() => throw new InvalidOperationException());
            });
        }

        var next = new object();
        bag.AdmitOwned(next, Finding());
        var expected = bag.Admitted[^1];
        Withheld replayed;
        using(bag.ReplayOwnedFailures())
        {
            replayed = bag.Withhold(() => bag.AdmitOwned(next, Finding()));
        }

        Assert.Equal(expected.Ordinal, Assert.Single(replayed.Occurrences).Ordinal);
        replayed.Discard();
    }

    private static Admission Finding()
    {
        return DiagnosticFactory.Create(
            ParserDiagnostics.MultipleAccessModifiers, new Absent(AbsenceReason.TestOrigin), PipelineStage.Parsing, []);
    }
}