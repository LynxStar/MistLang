using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class ProjectReferenceGraphTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Diamond_ValidAndMismatchedEdgesAreValidatedInEitherOrder(bool mismatchFirst)
    {
        var graph = new ReferenceGraph();
        var leaf = graph.Add("Leaf");
        var target = graph.Add("Target", leaf);
        var valid = graph.Add("Valid", target);
        var invalid = graph.Add("Invalid", target with { Name = "Alias" });
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, mismatchFirst ? [invalid, valid] : [valid, invalid]);

        AssertMismatch(Assert.Single(diagnostics.ToImmutableArray()), "Alias", "Target", target.Path);
        Assert.Equal(mismatchFirst ? ["Invalid", "Leaf", "Target", "Valid"] : new[] { "Leaf", "Target", "Valid", "Invalid" },
            result.Select(project => project.Configuration.Name));
        Assert.All(graph.LoadCounts.Values, count => Assert.Equal(1, count));
        Assert.Equal(4, graph.LoadCounts.Count);
        Assert.All(graph.DiscoveryCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void MismatchedEdge_DoesNotLoadDependenciesOrDiscoverSources()
    {
        var graph = new ReferenceGraph();
        var leaf = graph.Add("Leaf");
        var target = graph.Add("Target", leaf);
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, target with { Name = "Alias" });

        Assert.Empty(result);
        AssertMismatch(Assert.Single(diagnostics.ToImmutableArray()), "Alias", "Target", target.Path);
        Assert.Equal([target.Path], graph.LoadOrder);
        Assert.Empty(graph.DiscoveryCounts);
    }

    [Fact]
    public void MismatchedEdge_DoesNotPoisonLaterValidEdgeOrTraverseBeforeIt()
    {
        var graph = new ReferenceGraph();
        var leaf = graph.Add("Leaf");
        var target = graph.Add("Target", leaf);
        var valid = graph.Add("Valid", target);
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, target with { Name = "Alias" }, valid);

        AssertMismatch(Assert.Single(diagnostics.ToImmutableArray()), "Alias", "Target", target.Path);
        Assert.Equal([target.Path, valid.Path, leaf.Path], graph.LoadOrder);
        Assert.Equal(["Leaf", "Target", "Valid"], result.Select(project => project.Configuration.Name));
        Assert.All(graph.LoadCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void CorrectRepeatedEdges_LoadAndResolveOnceInDependencyOrder()
    {
        var graph = new ReferenceGraph();
        var leaf = graph.Add("Leaf");
        var target = graph.Add("Target", leaf, leaf);
        var left = graph.Add("Left", target);
        var right = graph.Add("Right", target);
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, left, target, right, left);

        Assert.Empty(diagnostics.ToImmutableArray());
        Assert.Equal([leaf.Path, target.Path, left.Path, right.Path], result.Select(project => project.CanonicalTomlPath));
        Assert.All(graph.LoadCounts.Values, count => Assert.Equal(1, count));
        Assert.Equal(4, graph.LoadCounts.Count);
        Assert.All(graph.DiscoveryCounts.Values, count => Assert.Equal(1, count));
        Assert.All(result, project => Assert.Equal(Path.Combine(project.ProjectFolder, "source.mist"), Assert.Single(project.SourcePaths)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DistinctBadNames_EachReportAMismatchEvenWhenTargetIsCompleted(bool validFirst)
    {
        var graph = new ReferenceGraph();
        var target = graph.Add("Target");
        var first = target with { Name = "FirstAlias" };
        var second = target with { Name = "SecondAlias" };
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, validFirst ? [target, first, second] : [first, second, target]);

        Assert.Equal(["Target"], result.Select(project => project.Configuration.Name));
        Assert.Collection(diagnostics.ToImmutableArray(),
            diagnostic => AssertMismatch(diagnostic, "FirstAlias", "Target", target.Path),
            diagnostic => AssertMismatch(diagnostic, "SecondAlias", "Target", target.Path));
        Assert.Equal(1, graph.LoadCounts[target.Path]);
    }

    [Theory]
    [InlineData("target")]
    [InlineData("TARGET")]
    public void ReferenceNames_AreCaseSensitive(string alias)
    {
        var graph = new ReferenceGraph();
        var target = graph.Add("Target");
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, target, target with { Name = alias });

        Assert.Equal(["Target"], result.Select(project => project.Configuration.Name));
        AssertMismatch(Assert.Single(diagnostics.ToImmutableArray()), alias, "Target", target.Path);
        Assert.Equal(1, graph.LoadCounts[target.Path]);
    }

    [Fact]
    public void FailedManifest_InDiamondLoadsAndReportsItsDiagnosticsOnce()
    {
        var graph = new ReferenceGraph();
        var failed = graph.AddFailure("Broken");
        var left = graph.Add("Left", failed);
        var right = graph.Add("Right", failed with { Name = "OtherAlias" });
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, left, right, failed);

        Assert.Equal(["Left", "Right"], result.Select(project => project.Configuration.Name));
        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(DriverDiagnostics.ProjectMissingName.Number, diagnostic.Number);
        Assert.Equal(failed.Path, diagnostic.Location.Path);
        Assert.All(graph.LoadCounts.Values, count => Assert.Equal(1, count));
        Assert.Equal(3, graph.LoadCounts.Count);
        Assert.DoesNotContain(Path.GetDirectoryName(failed.Path)!, graph.DiscoveryCounts.Keys);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadCache_IsScopedToOneResolution(bool fails)
    {
        var graph = new ReferenceGraph();
        var target = fails ? graph.AddFailure("Target") : graph.Add("Target");
        var firstBag = new DiagnosticBag();
        var secondBag = new DiagnosticBag();

        var firstResult = graph.Resolve(firstBag, target, target);
        var secondResult = graph.Resolve(secondBag, target, target);

        Assert.Equal(2, graph.LoadCounts[target.Path]);
        Assert.Equal(fails ? 0 : 1, firstResult.Length);
        Assert.Equal(firstResult.Select(project => project.CanonicalTomlPath), secondResult.Select(project => project.CanonicalTomlPath));
        Assert.Equal(fails ? 1 : 0, firstBag.ToImmutableArray().Length);
        Assert.Equal(firstBag.ToImmutableArray().ToArray(), secondBag.ToImmutableArray().ToArray());
    }

    [Fact]
    public void RootSelfCycle_ReportsExactChainWithoutReloadingRootAndContinuesSiblings()
    {
        var graph = new ReferenceGraph();
        var sibling = graph.Add("Sibling");
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, new ProjectReference("Root", graph.RootPath), sibling);

        AssertCycle(Assert.Single(diagnostics.ToImmutableArray()), graph.RootPath, graph.RootPath);
        Assert.Equal(["Sibling"], result.Select(project => project.Configuration.Name));
        Assert.Equal([sibling.Path], graph.LoadOrder);
    }

    [Fact]
    public void ReferencedSelfCycle_ReportsExactChainEvenWhenManifestIsCached()
    {
        var graph = new ReferenceGraph();
        var self = new ProjectReference("Self", ReferenceGraph.PathFor("Self"));
        _ = graph.Add("Self", self);
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, self, self);

        AssertCycle(Assert.Single(diagnostics.ToImmutableArray()), self.Path, self.Path);
        Assert.Equal([self.Path], result.Select(project => project.CanonicalTomlPath));
        Assert.Equal(1, graph.LoadCounts[self.Path]);
    }

    [Fact]
    public void LongerCycle_ReportsOnlyTheCycleChainAndPreservesPartialResults()
    {
        var graph = new ReferenceGraph();
        var a = new ProjectReference("A", ReferenceGraph.PathFor("A"));
        var c = graph.Add("C", a);
        var b = graph.Add("B", c);
        _ = graph.Add("A", b);
        var entry = graph.Add("Entry", a);
        var sibling = graph.Add("Sibling");
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, entry, sibling, a);

        AssertCycle(Assert.Single(diagnostics.ToImmutableArray()), a.Path, b.Path, c.Path, a.Path);
        Assert.Equal(["C", "B", "A", "Entry", "Sibling"], result.Select(project => project.Configuration.Name));
        Assert.All(graph.LoadCounts.Values, count => Assert.Equal(1, count));
    }

    [Fact]
    public void TransitiveRootCycle_ReportsChainWithoutLoadingRoot()
    {
        var graph = new ReferenceGraph();
        var child = graph.Add("Child", new ProjectReference("Root", graph.RootPath));
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, child);

        AssertCycle(Assert.Single(diagnostics.ToImmutableArray()), graph.RootPath, child.Path, graph.RootPath);
        Assert.Equal([child.Path], result.Select(project => project.CanonicalTomlPath));
        Assert.Equal([child.Path], graph.LoadOrder);
    }

    [Fact]
    public void MismatchedEdge_ToActiveTargetIsValidatedBeforeCycleDetection()
    {
        var graph = new ReferenceGraph();
        var a = new ProjectReference("A", ReferenceGraph.PathFor("A"));
        var b = graph.Add("B", a with { Name = "Alias" });
        _ = graph.Add("A", b);
        var diagnostics = new DiagnosticBag();

        var result = graph.Resolve(diagnostics, a);

        AssertMismatch(Assert.Single(diagnostics.ToImmutableArray()), "Alias", "A", a.Path);
        Assert.Equal(["B", "A"], result.Select(project => project.Configuration.Name));
        Assert.All(graph.LoadCounts.Values, count => Assert.Equal(1, count));
    }

    private static void AssertMismatch(Diagnostic diagnostic, string declaredName, string actualName, string path)
    {
        Assert.Equal(DriverDiagnostics.ProjectReferenceNameMismatch.Number, diagnostic.Number);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(PipelineStage.Driver, diagnostic.Stage);
        Assert.Equal(path, diagnostic.Location.Path);
        Assert.Equal($"Reference key '{declaredName}' does not match '{actualName}', the name the Mist project '{path}' produces.", diagnostic.Message);
    }

    private static void AssertCycle(Diagnostic diagnostic, params string[] chain)
    {
        Assert.Equal(DriverDiagnostics.ProjectCircularReference.Number, diagnostic.Number);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(chain[^1], diagnostic.Location.Path);
        Assert.Equal($"Circular project reference detected: {string.Join(" \u2192 ", chain)}.", diagnostic.Message);
    }

    private sealed class ReferenceGraph
    {
        private readonly Dictionary<string, ProjectConfiguration?> manifests = new(StringComparer.Ordinal);

        public string RootPath { get; } = PathFor("Root");
        public Dictionary<string, int> LoadCounts { get; } = new(StringComparer.Ordinal);
        public List<string> LoadOrder { get; } = [];
        public Dictionary<string, int> DiscoveryCounts { get; } = new(StringComparer.Ordinal);

        public ProjectReference Add(string name, params ProjectReference[] references)
        {
            var path = PathFor(name);
            manifests.Add(path, new ProjectConfiguration(name, name, [.. references]));
            return new ProjectReference(name, path);
        }

        public ProjectReference AddFailure(string name)
        {
            var path = PathFor(name);
            manifests.Add(path, null);
            return new ProjectReference(name, path);
        }

        public ImmutableArray<ResolvedProjectReference> Resolve(DiagnosticBag diagnostics, params ProjectReference[] references)
        {
            return ProjectReferenceResolver.Resolve(Path.GetDirectoryName(RootPath)!, [.. references], diagnostics, LoadManifest, FindSourcePaths);
        }

        public static string PathFor(string name)
        {
            return Path.GetFullPath(Path.Combine("reference-graph", name, ProjectFileParser.FileName));
        }

        private ProjectConfiguration? LoadManifest(string path, DiagnosticBag diagnostics)
        {
            LoadCounts[path] = LoadCounts.GetValueOrDefault(path) + 1;
            LoadOrder.Add(path);
            var configuration = manifests[path];
            if(configuration is null)
            {
                diagnostics.Report(DriverDiagnostics.ProjectMissingName, default, path, path);
            }

            return configuration;
        }

        private ImmutableArray<string> FindSourcePaths(string folder)
        {
            DiscoveryCounts[folder] = DiscoveryCounts.GetValueOrDefault(folder) + 1;
            return [Path.Combine(folder, "source.mist")];
        }
    }
}