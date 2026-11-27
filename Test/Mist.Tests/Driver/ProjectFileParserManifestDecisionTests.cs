using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

/// <summary>
/// book/src/tooling/project-configuration.md, "Error Behavior" and "The [diagnostics] Table". Expected
/// messages are written out from the descriptor templates, not rendered through them.
/// </summary>
[Trait("Tier", "2")]
public sealed class ProjectFileParserManifestDecisionTests : IDisposable
{
    private const string DuplicateUsingPromoted = """
        name = "App"

        [global_usings]
        namespaces = ["System", "System"]

        [diagnostics]
        warnings_as_errors = ["MIST0082"]
        """;

    private readonly string projectDir = Path.Combine(Path.GetTempPath(), $"mist_manifest_decisions_{Guid.NewGuid():N}");

    public ProjectFileParserManifestDecisionTests()
    {
        _ = Directory.CreateDirectory(projectDir);
    }

    private string ManifestPath => Path.Combine(projectDir, ProjectFileParser.FileName);

    public void Dispose()
    {
        Directory.Delete(projectDir, recursive: true);
    }

    public static TheoryData<string, string> Refusals => new()
    {
        { "name = 5", "MIST0052" },
        { "name = \"   \"", "MIST0053" },
        { "name = \"App\"\nentry_point = \" \"", "MIST0058" },
        { "name = \"App\"\noutput_type = 5", "MIST0059" },
        { "name = \"App\"\nreferences = 5", "MIST0066" },
        { "name = \"App\"\n[references]\nLib = \"../Lib\"", "MIST0065" },
        { "name = \"App\"\n[references]\n\" \" = { path = \"../Lib\" }", "MIST0068" },
        { "name = \"App\"\n[references]\n\"Bad Ref\" = { path = \"../Lib\" }", "MIST0067" },
        { "name = \"App\"\nglobal_usings = 5", "MIST0076" },
        { "name = \"App\"\n[global_usings]\nnamespaces = [\"Company.Tools.9Invalid\"]", "MIST0081" },
        { "name = \"App\"\ndiagnostics = 3", "MIST0103" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void AnInvalidManifestField_IsOneErrorAndYieldsNoConfiguration(string manifest, string number)
    {
        File.WriteAllText(ManifestPath, manifest);
        var diagnostics = new DiagnosticBag();

        var configuration = ProjectFileParser.Parse(projectDir, diagnostics);

        Assert.Null(configuration);
        var occurrence = Assert.Single(diagnostics.Admitted);
        Assert.Equal(number, occurrence.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Equal(new FileOnly(ManifestPath), occurrence.Location);
    }

    [Fact]
    public void AnInvalidName_ListsEachOffendingCharacterOnceInOrder()
    {
        File.WriteAllText(ManifestPath, "name = \"a@b c@\"");
        var diagnostics = new DiagnosticBag();

        Assert.Null(ProjectFileParser.Parse(projectDir, diagnostics));

        var occurrence = Assert.Single(diagnostics.Admitted);
        Assert.Equal("MIST0054", occurrence.Number.Value);
        Assert.Equal(
            $"Project name in '{ManifestPath}' contains invalid characters: ' ', '@'. Only alphanumeric characters, hyphens, underscores, and periods are allowed.",
            occurrence.Message);
    }

    [Fact]
    public void AReferenceKey_IsKeptAt128CharactersAndRefusedAt129()
    {
        var atLimit = new string('k', 128);
        var overLimit = new string('k', 129);

        File.WriteAllText(ManifestPath, $"name = \"App\"\n[references]\n{atLimit} = {{ path = \"../Lib\" }}");
        var accepted = new DiagnosticBag();
        var configuration = ProjectFileParser.Parse(projectDir, accepted);

        File.WriteAllText(ManifestPath, $"name = \"App\"\n[references]\n{overLimit} = {{ path = \"../Lib\" }}");
        var refused = new DiagnosticBag();

        Assert.Null(ProjectFileParser.Parse(projectDir, refused));
        Assert.Empty(accepted.Admitted);
        Assert.Equal(atLimit, Assert.Single(configuration!.References).Name);
        var occurrence = Assert.Single(refused.Admitted);
        Assert.Equal("MIST0069", occurrence.Number.Value);
        Assert.Equal(
            $"Reference key '{overLimit}' in '{ManifestPath}' exceeds the maximum length of 128 characters (got 129).",
            occurrence.Message);
    }

    [Fact]
    public void AnUnreadableManifest_IsOneErrorNamingItsPath()
    {
        File.WriteAllText(ManifestPath, "name = \"App\"");
        var diagnostics = new DiagnosticBag();

        using(new FileStream(ManifestPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Null(ProjectFileParser.Parse(projectDir, diagnostics));
        }

        var occurrence = Assert.Single(diagnostics.Admitted);
        Assert.Equal("MIST0050", occurrence.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Equal(new FileOnly(ManifestPath), occurrence.Location);
        Assert.StartsWith($"Failed to read project file '{ManifestPath}': ", occurrence.Message);
    }

    [Fact]
    public void TheRootManifestsPolicy_GovernsTheManifestsOwnWarnings()
    {
        File.WriteAllText(ManifestPath, DuplicateUsingPromoted);
        var diagnostics = new DiagnosticBag();

        Assert.NotNull(ProjectFileParser.ParseRoot(projectDir, diagnostics));

        var occurrence = Assert.Single(diagnostics.Admitted);
        Assert.Same(DriverDiagnostics.GlobalUsingDuplicateNamespace, occurrence.Descriptor);
        Assert.Equal(DiagnosticSeverity.Error, occurrence.Severity);
        Assert.Contains(DriverDiagnostics.GlobalUsingDuplicateNamespace.Number, diagnostics.Operation.Policy.Promoted);
    }

    [Fact]
    public void AReferencedManifestsPolicy_ReachesNeitherTheOperationNorItsOwnWarnings()
    {
        File.WriteAllText(ManifestPath, DuplicateUsingPromoted);
        var diagnostics = new DiagnosticBag();
        var operationPolicy = diagnostics.Operation.Policy;

        var configuration = ProjectFileParser.Parse(projectDir, diagnostics);

        Assert.NotNull(configuration);
        Assert.Contains(DriverDiagnostics.GlobalUsingDuplicateNamespace.Number, configuration.Diagnostics.Promoted);
        Assert.Same(operationPolicy, diagnostics.Operation.Policy);
        var occurrence = Assert.Single(diagnostics.Admitted);
        Assert.Same(DriverDiagnostics.GlobalUsingDuplicateNamespace, occurrence.Descriptor);
        Assert.Equal(DiagnosticSeverity.Warning, occurrence.Severity);
    }
}