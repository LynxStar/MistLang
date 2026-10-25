using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "4")]
public sealed class ProjectReferenceManifestSeamTests : IDisposable
{
    private readonly string fixtureFolder = Path.Combine(Path.GetTempPath(), $"mist_reference_edges_{Guid.NewGuid():N}");

    [Fact]
    public void NamedManifestToResolver_NormalizedPathsStillValidateEachName()
    {
        var targetFolder = WriteProject("Target", """name = "Target" """);
        var rootFolder = WriteProject("Root", """
            name = "Root"
            [references]
            Target = { path = "../Target/./" }
            Alias = { path = "../Target/../Target/mist.toml" }
            """);
        var diagnostics = new DiagnosticBag();
        var root = ProjectFileParser.Parse(rootFolder, diagnostics);
        Assert.NotNull(root);
        var targetPath = Path.GetFullPath(Path.Combine(targetFolder, ProjectFileParser.FileName));
        Assert.All(root.References, reference => Assert.Equal(targetPath, reference.Path));

        var result = ProjectReferenceResolver.Resolve(rootFolder, root.References, diagnostics);

        Assert.Equal(targetPath, Assert.Single(result).CanonicalTomlPath);
        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(DriverDiagnostics.ProjectReferenceNameMismatch.Number, diagnostic.Number);
        Assert.Contains("'Alias'", diagnostic.Message);
        Assert.Contains("'Target'", diagnostic.Message);
        Assert.Equal(targetPath, diagnostic.CompilationUnitPath);
    }

    [Theory]
    [InlineData("version = \"1.0.0\"", "MIST_PROJECT_MISSING_NAME")]
    [InlineData("name = [", "MIST_PROJECT_TOML_ERROR")]
    public void ManifestParserToResolver_RepeatedFailedLoadsPropagateOneErrorAndKeepValidSiblings(string content, string expectedId)
    {
        var brokenFolder = WriteProject("Broken", content);
        _ = WriteProject("Valid", """name = "Valid" """);
        var rootFolder = WriteProject("Root", """
            name = "Root"
            [references]
            Broken = { path = "../Broken" }
            Valid = { path = "../Valid" }
            Alias = { path = "../Broken/mist.toml" }
            """);
        var diagnostics = new DiagnosticBag();
        var root = ProjectFileParser.Parse(rootFolder, diagnostics);
        Assert.NotNull(root);

        var result = ProjectReferenceResolver.Resolve(rootFolder, root.References, diagnostics);

        Assert.Equal("Valid", Assert.Single(result).Configuration.Name);
        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(expectedId, diagnostic.Number.Value);
        Assert.Equal(DiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(Path.Combine(brokenFolder, ProjectFileParser.FileName), diagnostic.CompilationUnitPath);
    }

    public void Dispose()
    {
        Directory.Delete(fixtureFolder, recursive: true);
    }

    private string WriteProject(string name, string content)
    {
        var folder = Path.Combine(fixtureFolder, name);
        _ = Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(folder, ProjectFileParser.FileName), content);
        return folder;
    }
}