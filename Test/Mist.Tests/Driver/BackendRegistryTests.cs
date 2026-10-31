using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class BackendRegistryTests
{
    [Fact]
    public void DefaultRegistry_ResolvesOnlyCSharpWithoutClaims()
    {
        var registry = new BackendRegistry();
        var backend = Assert.IsAssignableFrom<IBackend>(registry.Resolve(BackendId.CSharp));

        Assert.Equal(BackendId.CSharp, backend.Id);
        Assert.Empty(backend.SourceTranslators);
        Assert.Empty(backend.ClaimedExtensions);
        Assert.Null(registry.Resolve(new BackendId("probe")));
    }

    [Fact]
    public void Registry_UsesRegisteredIdsRatherThanTheBuiltInId()
    {
        IBackend backend = new ProbeBackend();
        var registry = new BackendRegistry([backend]);

        Assert.Same(backend, registry.Resolve(new BackendId("probe")));
        Assert.Null(registry.Resolve(BackendId.CSharp));
    }

    private sealed class ProbeBackend : IBackend
    {
        public BackendId Id => new("probe");
        public ImmutableArray<ISourceTranslator> SourceTranslators => [];
    }
}