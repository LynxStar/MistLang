namespace Mist.Tests.Helpers;

/// <summary>
/// Tests that assert on elapsed wall-clock time. xunit runs this collection alone, after every
/// parallel collection has finished: a stopwatch read beside the rest of the suite measures the
/// testhost's stop-the-world garbage collections, which are most of the time it reports there.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WallClockCollection
{
    public const string Name = "Wall clock";
}