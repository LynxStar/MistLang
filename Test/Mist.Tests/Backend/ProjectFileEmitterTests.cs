using System.Collections.Immutable;
using System.Xml.Linq;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

/// <summary>
/// Tier 1: what the manifest declares survives into the generated project file.
/// <para>
/// NOTHING IN THE COMPILER READS THAT FILE BACK, which is why it needs its own test. Emission
/// compiles through Roslyn in process, so a project file declaring nothing still yields a working
/// DLL and every sample stays green -- the file is what the USER builds afterwards, and its
/// contents are unobservable until they do. Parsing rather than string-matching also pins the one
/// thing a fragment-assembling emitter can break invisibly: that the result is still XML.
/// </para>
/// </summary>
public sealed class ProjectFileEmitterTests
{
    private static XDocument Emit(
        ProjectOutputType outputType = ProjectOutputType.Executable,
        ImmutableArray<NuGetPackageReference> packages = default,
        ImmutableArray<FrameworkReference> frameworkReferences = default,
        ImmutableArray<CsprojProperty> csprojProperties = default,
        bool carriesEntryPoint = false)
    {
        var options = new CompilerOptions(
            ProjectFolder: "probe",
            SourcePaths: ImmutableArray<string>.Empty,
            Backend: new BackendId("csharp"),
            OutputDirectory: "probe/out",
            BinDirectory: "probe/bin",
            ProjectName: "Probe",
            NamespaceRoot: "Probe",
            EnabledAnalyzerIds: ImmutableArray<string>.Empty,
            Packages: packages,
            FrameworkReferences: frameworkReferences,
            CsprojProperties: csprojProperties,
            OutputType: outputType);

        return XDocument.Parse(ProjectFileEmitter.Emit(options, carriesEntryPoint));
    }

    private static IEnumerable<string> Items(XDocument project, string element, params string[] attributes)
    {
        return project
            .Descendants(element)
            .Select(e => string.Join(" ", attributes.Select(a => e.Attribute(a)?.Value)));
    }

    [Fact]
    public void DeclaredPackages_BecomePackageReferences()
    {
        var project = Emit(packages: [
            new NuGetPackageReference("xunit", "2.9.3"),
            new NuGetPackageReference("Microsoft.NET.Test.Sdk", "17.12.0")]);

        Assert.Equal(
            ["xunit 2.9.3", "Microsoft.NET.Test.Sdk 17.12.0"],
            Items(project, "PackageReference", "Include", "Version"));
    }

    [Fact]
    public void DeclaredFrameworkReferences_BecomeFrameworkReferences()
    {
        var project = Emit(frameworkReferences: [new FrameworkReference("Microsoft.AspNetCore.App")]);

        Assert.Equal(
            ["Microsoft.AspNetCore.App"],
            Items(project, "FrameworkReference", "Include"));
    }

    [Fact]
    public void DeclaredProperties_RoundTripThroughXmlEscaping()
    {
        var project = Emit(csprojProperties: [new CsprojProperty("DefineConstants", "A<B&C")]);

        Assert.Equal("A<B&C", project.Descendants("DefineConstants").Single().Value);
    }

    [Fact]
    public void LibraryOutputCarryingAMain_SuppressesTheTestSdkGeneratedEntryPoint()
    {
        Assert.Equal(
            "false",
            Emit(ProjectOutputType.Library, carriesEntryPoint: true).Descendants("GenerateProgramFile").Single().Value);
    }

    [Fact]
    public void LibraryOutputCarryingNoMain_LeavesTheTestSdkGeneratedEntryPointAlone()
    {
        Assert.Empty(Emit(ProjectOutputType.Library).Descendants("GenerateProgramFile"));
    }

    [Fact]
    public void ExecutableOutput_LeavesTheTestSdkGeneratedEntryPointAlone()
    {
        Assert.Empty(Emit(ProjectOutputType.Executable, carriesEntryPoint: true).Descendants("GenerateProgramFile"));
    }

    [Fact]
    public void NothingDeclared_EmitsNoItemGroup()
    {
        Assert.Empty(Emit().Descendants("ItemGroup"));
    }
}