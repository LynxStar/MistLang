using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "2")]
public sealed class GeneratedReferenceOverlayTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void GeneratedProjectsFollowTheConsumersOrdinaryDependencies(bool transitive)
    {
        var library = new ProjectConfiguration("Library", "Library", []);
        var consumer = new ProjectConfiguration("Consumer", "Consumer", [new ProjectReference("Library", "/Library/mist.toml")]);
        var rootReferences = transitive ? ImmutableArray.Create(new ProjectReference("Consumer", "/Consumer/mist.toml")) : consumer.References;
        var generated = new ResolvedProjectReference("/Consumer/out/generated/G/mist.toml", "/Consumer/out/generated/G",
            new ProjectConfiguration("Consumer.Generated.G", "Consumer.Generated.G", [], OutputType: ProjectOutputType.Library), ["/Consumer/out/generated/G/G.mist"]);
        var overlay = ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>>.Empty.Add("/Consumer/mist.toml", [generated]);
        var diagnostics = new DiagnosticBag();
        var order = ProjectReferenceResolver.ResolveAll(transitive ? "/Root" : "/Consumer", rootReferences, diagnostics,
            (path, _) => path == "/Library/mist.toml" ? library : consumer, _ => [], overlay);
        Assert.Equal(transitive ? ["Library", "Consumer.Generated.G", "Consumer"] : ["Library", "Consumer.Generated.G"],
            order.Projects.Select(project => project.Configuration.Name));
        Assert.Same(generated, order.Projects[1]);
        Assert.Empty(diagnostics.Admitted);
    }
}