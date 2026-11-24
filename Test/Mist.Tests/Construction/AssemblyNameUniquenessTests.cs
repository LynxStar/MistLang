using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model;
using Mist.Tests.Helpers;
using Model = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Tests.Construction;

/// <summary>
/// MIST0384: two source-bearing assemblies in one compilation may not share a name (book diagnostics
/// reference; ProgramModelDiagnostics.DuplicateAssemblyName). The projects are handed to the builder
/// directly, with no units, so only the assembly registration and its check run. The driver supplies
/// each referenced project's name and canonical manifest path; which manifest field it reads the name
/// from is the driver's concern, not this check's.
/// </summary>
[Trait("Tier", "2")]
public sealed class AssemblyNameUniquenessTests
{
    private static readonly string Root = OperatingSystem.IsWindows() ? @"C:\work" : "/work";

    [Fact]
    public void TwoReferencedProjectsSharingANameAreReportedOnceAtTheSecondProjectsManifest()
    {
        var first = Manifest("First");
        var second = Manifest("Second");

        var diagnostics = Build("App", hasStandardLibrary: false, Project("Shared", first), Project("Shared", second));

        var duplicate = Assert.Single(diagnostics, d => d.Number.Value == "MIST0384");
        Assert.Equal(DiagnosticSeverity.Error, duplicate.Severity);
        Assert.Equal(new ProjectLocation(second, null), duplicate.Location);
        Assert.Equal(new NameValue("Shared"), Assert.Single(duplicate.Arguments));
    }

    [Fact]
    public void AReferencedProjectNamedLikeTheCompiledProjectIsReportedAtTheReferencesManifest()
    {
        var reference = Manifest("Other");

        var diagnostics = Build("App", hasStandardLibrary: false, Project("App", reference));

        var duplicate = Assert.Single(diagnostics, d => d.Number.Value == "MIST0384");
        Assert.Equal(new ProjectLocation(reference, null), duplicate.Location);
        Assert.Equal(new NameValue("App"), Assert.Single(duplicate.Arguments));
    }

    /// <summary>
    /// The standard library is source-bearing, so a project named after it collides with it. It is
    /// registered after the compiled project and has no manifest, so the finding has nowhere to point.
    /// </summary>
    [Fact]
    public void ACompiledProjectNamedStandardCollidesWithTheStandardLibraryWhichHasNoManifest()
    {
        var diagnostics = Build(MistAssembly.StandardName, hasStandardLibrary: true);

        var duplicate = Assert.Single(diagnostics, d => d.Number.Value == "MIST0384");
        Assert.Equal(new Absent(AbsenceReason.Unavailable), duplicate.Location);
        Assert.Equal(new NameValue(MistAssembly.StandardName), Assert.Single(duplicate.Arguments));
    }

    /// <summary>
    /// Control for the cases above: a project is identified by its canonical manifest path, so one
    /// project recorded twice is one assembly, and distinct names never collide.
    /// </summary>
    [Fact]
    public void OneProjectRecordedTwiceAndDistinctlyNamedProjectsReportNothing()
    {
        var shared = Manifest("Shared");

        var diagnostics = Build(
            "App", hasStandardLibrary: true, Project("Shared", shared), Project("Shared", shared), Project("Other", Manifest("Other")));

        Assert.DoesNotContain(diagnostics, d => d.Number.Value == "MIST0384");
    }

    private static ImmutableArray<Diagnostic> Build(string projectName, bool hasStandardLibrary, params MistProject[] projects)
    {
        var options = new CompilerOptions(
            ProjectFolder: Path.Combine(Root, "App"),
            SourcePaths: [],
            Backend: new BackendId("csharp"),
            OutputDirectory: Path.Combine(Root, "App", "out"),
            BinDirectory: Path.Combine(Root, "App", "bin"),
            ProjectName: projectName,
            NamespaceRoot: projectName);
        var compilation = new MistCompilation([])
        {
            Projects = [.. projects],
            HasStandardLibrary = hasStandardLibrary,
        };
        var diagnostics = new DiagnosticBag();

        _ = Assert.IsType<Model>(ProgramModelBuilder.Build(compilation, options, diagnostics, NullAmbientTypeProvider.Instance));

        return diagnostics.ToImmutableArray();
    }

    private static MistProject Project(string name, string manifest)
    {
        return new MistProject(name, Path.GetDirectoryName(manifest)!, manifest);
    }

    private static string Manifest(string folder)
    {
        return Path.Combine(Root, folder, "mist.toml");
    }
}