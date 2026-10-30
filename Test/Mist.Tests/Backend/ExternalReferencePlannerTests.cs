using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class ExternalReferencePlannerTests
{
    private const string BridgeOutput = "/repo/Bridge/bin/Bridge.dll";
    private const string StaleLib = "/repo/Bridge/bin/Lib.dll";
    private const string StaleApp = "/repo/Bridge/bin/App.dll";
    private const string ClosureJson = "/repo/Bridge/bin/Newtonsoft.Json.dll";
    private const string PackageJson = "/nuget/newtonsoft.json/lib/Newtonsoft.Json.dll";

    private static readonly ComposedProject Root = new("App", "/repo/App/mist.toml");
    private static readonly ComposedProject Lib = new("Lib", "/repo/Lib/mist.toml");

    private readonly StubMetadata metadata = new();
    private readonly DiagnosticBag diagnostics = new();

    [Fact]
    public void WithoutExternalAssembliesTheSuppliedPathsAreUnchanged()
    {
        var plan = Plan([PackageJson], externals: []);

        Assert.Equal([PackageJson], plan.Paths.AsEnumerable());
        Assert.Empty(plan.Facades);
        Assert.False(plan.Failed);
    }

    [Fact]
    public void AClosureMemberNamedAfterAComposedProjectIsShadowed()
    {
        var plan = Plan([], [Bridge([StaleLib, StaleApp])]);

        Assert.Equal([BridgeOutput], plan.Paths.AsEnumerable());
    }

    [Fact]
    public void AReferencedComposedProjectGetsAFacade()
    {
        metadata.References[BridgeOutput] = ["System.Runtime", "Lib"];

        var plan = Plan([], [Bridge([StaleLib])]);

        Assert.Equal([Lib], plan.Facades.AsEnumerable());
        Assert.False(plan.Failed);
    }

    [Fact]
    public void AnUnreferencedComposedProjectGetsNoFacade()
    {
        metadata.References[BridgeOutput] = ["System.Runtime"];

        var plan = Plan([], [Bridge([StaleLib])]);

        Assert.Empty(plan.Facades);
    }

    [Fact]
    public void AReferenceToTheRootProjectIsACycle()
    {
        metadata.References[BridgeOutput] = ["App"];

        var plan = Plan([], [Bridge([])]);

        Assert.True(plan.Failed);
        Assert.Empty(plan.Facades);
        var cycle = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0075", cycle.Number.Value);
        Assert.Contains("'Bridge'", cycle.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosureMemberSuppliedAtAnotherVersionConflicts()
    {
        metadata.Versions[PackageJson] = new Version(13, 0, 0, 0);
        metadata.Versions[ClosureJson] = new Version(12, 0, 0, 0);

        var plan = Plan([PackageJson], [Bridge([ClosureJson])]);

        Assert.True(plan.Failed);
        Assert.Equal([PackageJson, BridgeOutput], plan.Paths.AsEnumerable());
        var conflict = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0004", conflict.Number.Value);
        Assert.Contains(ClosureJson, conflict.Message, StringComparison.Ordinal);
        Assert.Contains(PackageJson, conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void AClosureMemberSuppliedAtTheSameVersionIsDeployedOnce()
    {
        metadata.Versions[PackageJson] = new Version(13, 0, 0, 0);
        metadata.Versions[ClosureJson] = new Version(13, 0, 0, 0);

        var plan = Plan([PackageJson], [Bridge([ClosureJson])]);

        Assert.False(plan.Failed);
        Assert.Equal([PackageJson, BridgeOutput], plan.Paths.AsEnumerable());
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void AnExternalAssemblyAlreadySuppliedIsNeitherJudgedAgainstItselfNorAddedTwice()
    {
        metadata.Versions[BridgeOutput] = new Version(1, 0, 0, 0);

        var plan = Plan([BridgeOutput], [Bridge([])]);

        Assert.False(plan.Failed);
        Assert.Equal([BridgeOutput], plan.Paths.AsEnumerable());
        Assert.Empty(metadata.VersionReads);
    }

    [Fact]
    public void ASharedFrameworkTheManifestDoesNotDeclareIsMissing()
    {
        const string config = "/repo/Bridge/bin/Bridge.runtimeconfig.json";
        metadata.RuntimeConfigs[config] =
            """{ "runtimeOptions": { "frameworks": [ { "name": "Microsoft.NETCore.App" }, { "name": "Microsoft.AspNetCore.App" } ] } }""";

        var plan = Plan([], [Bridge([]) with { RuntimeConfigPath = config }]);

        Assert.True(plan.Failed);
        var missing = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal("MIST0005", missing.Number.Value);
        Assert.Contains("Microsoft.AspNetCore.App", missing.Message, StringComparison.Ordinal);
    }

    private ReferencePlan Plan(ImmutableArray<string> supplied, ImmutableArray<ExternalAssembly> externals)
    {
        return ExternalReferencePlanner.Plan("App", supplied, externals, [Root, Lib], [], metadata, diagnostics);
    }

    private static ExternalAssembly Bridge(ImmutableArray<string> closure)
    {
        return new ExternalAssembly("Bridge", BridgeOutput, closure, null, ReferenceKind.CSharpProject);
    }

    private sealed class StubMetadata : IAssemblyMetadataSource
    {
        public Dictionary<string, ImmutableArray<string>> References { get; } = [];

        public Dictionary<string, Version> Versions { get; } = [];

        public Dictionary<string, string> RuntimeConfigs { get; } = [];

        public List<string> VersionReads { get; } = [];

        public ImmutableArray<string> ReferencedNames(string path)
        {
            return References.GetValueOrDefault(path, []);
        }

        public Version? AssemblyVersion(string path)
        {
            VersionReads.Add(path);
            return Versions.GetValueOrDefault(path);
        }

        public string? RuntimeConfig(string path)
        {
            return RuntimeConfigs.GetValueOrDefault(path);
        }
    }
}