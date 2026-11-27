using Mist.Common.Collections;

namespace Mist.Tests.Common;

[Trait("Tier", "1")]
public sealed class ImmutableOrderedDictionarySnapshotTests
{
    [Fact]
    public void SnapshotEnumeratesOriginalEntriesInInsertionOrderAfterBuilderGrows()
    {
        var builder = new ImmutableOrderedDictionary<int, string>.Builder();
        builder.Add(30, "first");
        builder.Add(10, "second");
        builder.Add(20, "third");

        var snapshot = builder.ToImmutable();
        builder.Add(15, "later");

        Assert.Equal([(30, "first"), (10, "second"), (20, "third")],
            snapshot.Select(entry => (entry.Key, entry.Value)));
    }
}