using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "3")]
public sealed class ProjectFileParserReferenceKindTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "mist-reference-kind-" + Guid.NewGuid().ToString("N"));
    private readonly string app;

    public ProjectFileParserReferenceKindTests()
    {
        app = Path.Combine(root, "App");
        _ = Directory.CreateDirectory(app);
    }

    public void Dispose()
    {
        Directory.Delete(root, recursive: true);
    }

    [Theory]
    [InlineData("../Bridge/Bridge.csproj", ReferenceKind.CSharpProject)]
    [InlineData("../libs/Widgets.DLL", ReferenceKind.Assembly)]
    [InlineData("../Lib/mist.toml", ReferenceKind.MistProject)]
    public void ADirectPath_CarriesItsKind_WhetherOrNotTheFileExistsYet(string path, ReferenceKind kind)
    {
        var reference = Assert.Single(Parse(path).References);

        Assert.Equal(kind, reference.Kind);
        Assert.Equal(Path.GetFullPath(Path.Combine(app, path)), reference.Path);
        Assert.False(reference.IsDirectory);
    }

    [Fact]
    public void AnExistingDirectory_IsLeftForTheResolverToList()
    {
        _ = Directory.CreateDirectory(Path.Combine(root, "App.Migrations"));

        var reference = Assert.Single(Parse("../App.Migrations").References);

        Assert.Equal(new ProjectReference("Ref", Path.Combine(root, "App.Migrations", "mist.toml"), ReferenceKind.MistProject, IsDirectory: true), reference);
    }

    [Theory]
    [InlineData("../Missing")]
    [InlineData("../libs/Missing.txt")]
    public void AMissingPathWithNoReferenceableExtension_IsAMistProjectAsBefore(string path)
    {
        var reference = Assert.Single(Parse(path).References);

        Assert.Equal(new ProjectReference("Ref", Path.Combine(Path.GetFullPath(Path.Combine(app, path)), "mist.toml")), reference);
    }

    [Fact]
    public void AnExistingFileWithAnotherExtension_IsRefused()
    {
        var notes = Path.Combine(root, "libs", "Foo.txt");
        _ = Directory.CreateDirectory(Path.GetDirectoryName(notes)!);
        File.WriteAllText(notes, "");
        var diagnostics = new DiagnosticBag();

        Assert.Null(ParseWith("../libs/Foo.txt", diagnostics));

        var diagnostic = Assert.Single(diagnostics.ToImmutableArray());
        Assert.Equal(DriverDiagnostics.ReferenceUnsupportedPath.Id, diagnostic.Id);
        Assert.Equal(Path.Combine(app, ProjectFileParser.FileName), diagnostic.CompilationUnitPath);
        Assert.Equal($"Reference 'Ref' names '{notes}', which is not a mist.toml, a .csproj or a .dll.", diagnostic.Message);
    }

    private ProjectConfiguration Parse(string path)
    {
        var diagnostics = new DiagnosticBag();
        var configuration = ParseWith(path, diagnostics);

        Assert.False(diagnostics.HasErrors);
        Assert.NotNull(configuration);
        return configuration;
    }

    private ProjectConfiguration? ParseWith(string path, DiagnosticBag diagnostics)
    {
        File.WriteAllText(Path.Combine(app, ProjectFileParser.FileName), $"name = \"App\"\n\n[references]\nRef = {{ path = \"{path}\" }}\n");
        return ProjectFileParser.Parse(app, diagnostics);
    }
}