using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Tier 1: the dependency set a compilation resolves is the union over every manifest in it.
/// <para>
/// A REFERENCED project's declarations are invisible to every stdout baseline until they are
/// missing, and then the failure is an unresolved type three stages later. The version rule is
/// pinned here because no sample declares one package twice: the highest version declared wins,
/// whichever manifest declared it, because a declared version is a floor.
/// </para>
/// </summary>
public sealed class ProjectDependencyUnionTests
{
    private static CompilerOptions Root(
        ImmutableArray<NuGetPackageReference> packages = default,
        ImmutableArray<FrameworkReference> frameworkReferences = default)
    {
        return new CompilerOptions(
            ProjectFolder: "probe",
            SourcePaths: ImmutableArray<string>.Empty,
            Backend: new BackendId("csharp"),
            OutputDirectory: "probe/out",
            BinDirectory: "probe/bin",
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Packages: packages,
            FrameworkReferences: frameworkReferences);
    }

    private static ProjectConfiguration Referenced(
        string name,
        ImmutableArray<NuGetPackageReference> packages = default,
        ImmutableArray<FrameworkReference> frameworkReferences = default)
    {
        return new ProjectConfiguration(
            name,
            name,
            ImmutableArray<ProjectReference>.Empty,
            Packages: packages,
            FrameworkReferences: frameworkReferences);
    }

    private static IEnumerable<string> Describe(ImmutableArray<NuGetPackageReference> packages)
    {
        return packages.Select(p => $"{p.Name} {p.Version}");
    }

    [Fact]
    public void NoReferencedProjects_LeavesTheOptionsAlone()
    {
        var root = Root(packages: [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")]);

        var composed = ProjectDependencyUnion.Apply(root, ImmutableArray<ProjectConfiguration>.Empty);

        Assert.Same(root, composed);
    }

    [Fact]
    public void ReferencedDeclarations_FollowTheRootsInReferenceOrder()
    {
        var root = Root(
            packages: [new NuGetPackageReference("Root.Package", "1.0.0")],
            frameworkReferences: [new FrameworkReference("Microsoft.NETCore.App")]);
        var first = Referenced("First",
            packages: [new NuGetPackageReference("First.Package", "2.0.0")],
            frameworkReferences: [new FrameworkReference("Microsoft.AspNetCore.App")]);
        var second = Referenced("Second",
            packages: [new NuGetPackageReference("Second.Package", "3.0.0")]);

        var composed = ProjectDependencyUnion.Apply(root, [first, second]);

        Assert.Equal(
            ["Root.Package 1.0.0", "First.Package 2.0.0", "Second.Package 3.0.0"],
            Describe(composed.Packages));
        Assert.Equal(
            ["Microsoft.NETCore.App", "Microsoft.AspNetCore.App"],
            composed.FrameworkReferences.Select(f => f.Name));
    }

    [Fact]
    public void RootDeclaringNothing_TakesTheReferencedDeclarations()
    {
        var referenced = Referenced("Lib",
            packages: [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")],
            frameworkReferences: [new FrameworkReference("Microsoft.AspNetCore.App")]);

        var composed = ProjectDependencyUnion.Apply(Root(), [referenced]);

        Assert.Equal(["Newtonsoft.Json 13.0.3"], Describe(composed.Packages));
        Assert.Equal(["Microsoft.AspNetCore.App"], composed.FrameworkReferences.Select(f => f.Name));
    }

    [Fact]
    public void OnePackageDeclaredTwiceAtOneVersion_IsDeclaredOnce()
    {
        var root = Root(packages: [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")]);
        var referenced = Referenced("Lib", packages: [new NuGetPackageReference("newtonsoft.json", "13.0.3")]);

        var composed = ProjectDependencyUnion.Apply(root, [referenced]);

        Assert.Equal(["Newtonsoft.Json 13.0.3"], Describe(composed.Packages));
    }

    [Theory]
    [InlineData("12.0.1", "13.0.3", "13.0.3")]
    [InlineData("13.0.3", "12.0.1", "13.0.3")]
    [InlineData("13.0.3", "13.0.3-beta1", "13.0.3")]
    public void OnePackageDeclaredAtTwoVersions_TakesTheHighest(string rootVersion, string referencedVersion, string expected)
    {
        var root = Root(packages: [new NuGetPackageReference("Newtonsoft.Json", rootVersion)]);
        var referenced = Referenced("Lib", packages: [new NuGetPackageReference("Newtonsoft.Json", referencedVersion)]);

        var composed = ProjectDependencyUnion.Apply(root, [referenced]);

        Assert.Equal([$"Newtonsoft.Json {expected}"], Describe(composed.Packages));
    }

    [Fact]
    public void AVersionThatDoesNotParse_LeavesTheEarlierDeclarationForResolutionToReport()
    {
        var root = Root(packages: [new NuGetPackageReference("Newtonsoft.Json", "not-a-version")]);
        var referenced = Referenced("Lib", packages: [new NuGetPackageReference("Newtonsoft.Json", "13.0.3")]);

        var composed = ProjectDependencyUnion.Apply(root, [referenced]);

        Assert.Equal(["Newtonsoft.Json not-a-version"], Describe(composed.Packages));
    }

    [Fact]
    public void OneFrameworkDeclaredByTwoProjects_IsDeclaredOnce()
    {
        var root = Root(frameworkReferences: [new FrameworkReference("Microsoft.AspNetCore.App")]);
        var referenced = Referenced("Lib", frameworkReferences: [new FrameworkReference("Microsoft.AspNetCore.App")]);

        var composed = ProjectDependencyUnion.Apply(root, [referenced]);

        Assert.Equal(["Microsoft.AspNetCore.App"], composed.FrameworkReferences.Select(f => f.Name));
    }
}