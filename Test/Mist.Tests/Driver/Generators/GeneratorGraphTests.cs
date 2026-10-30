using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.Driver.Generators;

namespace Mist.Tests.Driver.Generators;

[Trait("Tier", "2")]
public sealed class GeneratorGraphTests
{
    [Fact]
    public void DiamondSharedAndNestedGeneratorsAreVisitedOnceBeforeConsumers()
    {
        var root = Node("Root", ["Left", "Right"], ["Shared"]);
        var left = Node("Left", [], ["Shared"]);
        var right = Node("Right", [], ["Shared"]);
        var shared = Node("Shared", [], ["Nested"]);
        var nested = Node("Nested", [], []);
        var bag = new DiagnosticBag();
        var loads = new List<string>();
        var ordered = GeneratorGraph.Order(root, path =>
        {
            loads.Add(path);
            return path == shared.Manifest ? shared.Configuration : nested.Configuration;
        }, node => node == root ? [left, right] : [], bag);
        Assert.Equal(["Nested", "Shared", "Left", "Right", "Root"], ordered.Select(node => node.Configuration.Name));
        Assert.Equal([shared.Manifest, nested.Manifest], loads);
        Assert.Empty(bag.Admitted);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectAndArbitraryMixedCyclesAreReportedOnce(bool transitive)
    {
        var root = Node("Consumer", [], ["G"]);
        var generator = Node("G", [transitive ? "Library" : "Consumer"], []);
        var library = Node("Library", ["Consumer"], []);
        var bag = new DiagnosticBag();
        _ = GeneratorGraph.Order(root, _ => generator.Configuration,
            node => node.Configuration.Name == "G" ? [library, root] : [], bag);
        var diagnostic = Assert.Single(bag.Admitted);
        Assert.Same(DriverDiagnostics.GeneratorCycle, diagnostic.Descriptor);
        Assert.Contains(root.Manifest, diagnostic.Message);
        Assert.Contains(generator.Manifest, diagnostic.Message);
        if(transitive)
        {
            Assert.Contains(library.Manifest, diagnostic.Message);
        }
    }

    [Fact]
    public void SelfCycleIsReported()
    {
        var root = Node("G", [], ["G"]);
        var bag = new DiagnosticBag();
        _ = GeneratorGraph.Order(root, _ => throw new InvalidOperationException(), _ => [], bag);
        Assert.Same(DriverDiagnostics.GeneratorCycle, Assert.Single(bag.Admitted).Descriptor);
    }

    [Fact]
    public void OrdinaryAndExternalEdgesNeverCallTheGeneratorLoader()
    {
        var ordinary = Node("Ordinary", [], []);
        var root = Node("Root", ["Ordinary"], []);
        root = root with { Configuration = root.Configuration with
        {
            References = root.Configuration.References.Add(new ProjectReference("CSharp", "/CSharp.csproj", ReferenceKind.CSharpProject))
                .Add(new ProjectReference("Assembly", "/Assembly.dll", ReferenceKind.Assembly))
        } };
        var bag = new DiagnosticBag();
        var order = GeneratorGraph.Order(root, _ => throw new InvalidOperationException("ordinary loader called"),
            node => node.Manifest == root.Manifest ? [ordinary] : [], bag);
        Assert.Equal(["Ordinary", "Root"], order.Select(node => node.Configuration.Name));
        Assert.Empty(bag.Admitted);
    }

    [Fact]
    public void OrdinaryOnlyCyclesRemainTheResolversResponsibility()
    {
        var root = Node("Root", ["Library"], []);
        var library = Node("Library", ["Root"], []);
        var bag = new DiagnosticBag();
        _ = GeneratorGraph.Order(root, _ => throw new InvalidOperationException(), _ => [root, library], bag);
        Assert.Empty(bag.Admitted);
    }

    private static GeneratorNode Node(string name, string[] ordinary, string[] generators)
    {
        var manifest = $"/{name}/mist.toml";
        return new GeneratorNode(manifest, new ProjectConfiguration(name, name,
            [.. ordinary.Select(target => new ProjectReference(target, $"/{target}/mist.toml"))],
            Generators: [.. generators.Select(target => new GeneratorReference(target, $"/{target}/mist.toml", [], [], TimeSpan.FromSeconds(600)))]));
    }
}