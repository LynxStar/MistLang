using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class ReferenceClassifierTests
{
    private const string Folder = "/work/App.Migrations";

    [Theory]
    [InlineData("/work/Lib/mist.toml", ReferenceKind.MistProject)]
    [InlineData("/work/Bridge/Bridge.csproj", ReferenceKind.CSharpProject)]
    [InlineData("/work/libs/Widgets.dll", ReferenceKind.Assembly)]
    [InlineData("/work/Bridge/Bridge.CSPROJ", ReferenceKind.CSharpProject)]
    [InlineData("/work/libs/Widgets.DLL", ReferenceKind.Assembly)]
    public void ADirectPath_IsClassifiedByItsFinalSegment(string path, ReferenceKind kind)
    {
        var classified = Assert.IsType<ReferenceClassified>(ReferenceClassifier.Classify(path, isDirectory: false, []));

        Assert.Equal(kind, classified.Kind);
        Assert.Equal(path, classified.ResolvedPath);
    }

    [Theory]
    [InlineData("/work/libs/Foo.txt", ".txt")]
    [InlineData("/work/Lib/project.toml", ".toml")]
    [InlineData("/work/Lib/MIST.TOML", ".TOML")]
    public void ADirectPathWithAnotherExtension_IsUnsupported(string path, string extension)
    {
        var unsupported = Assert.IsType<ReferenceUnsupportedExtension>(ReferenceClassifier.Classify(path, isDirectory: false, []));

        Assert.Equal(extension, unsupported.Extension);
    }

    [Theory]
    [InlineData("mist.toml", ReferenceKind.MistProject)]
    [InlineData("App.Migrations.csproj", ReferenceKind.CSharpProject)]
    [InlineData("App.Migrations.dll", ReferenceKind.Assembly)]
    public void ADirectoryWithOneCandidate_ResolvesToThatFile(string candidate, ReferenceKind kind)
    {
        string[] listing = ["README.md", candidate, "Program.cs", "dotnet-tools.json"];

        var classified = Assert.IsType<ReferenceClassified>(ReferenceClassifier.Classify(Folder, isDirectory: true, listing));

        Assert.Equal(kind, classified.Kind);
        Assert.Equal(Path.Combine(Folder, candidate), classified.ResolvedPath);
    }

    [Fact]
    public void ADirectoryWithNoCandidate_HasNoCandidate()
    {
        var result = ReferenceClassifier.Classify(Folder, isDirectory: true, ["README.md", "Program.cs"]);

        _ = Assert.IsType<ReferenceNoCandidate>(result);
    }

    [Fact]
    public void TwoCandidatesOfOneKind_AreAmbiguous_InOrdinalOrder()
    {
        var result = ReferenceClassifier.Classify(Folder, isDirectory: true, ["b.csproj", "README.md", "a.csproj"]);

        Assert.Equal<string>(["a.csproj", "b.csproj"], Assert.IsType<ReferenceAmbiguous>(result).Candidates);
    }

    [Fact]
    public void OneCandidateOfEachOfTwoKinds_IsAmbiguous()
    {
        var result = ReferenceClassifier.Classify(Folder, isDirectory: true, ["mist.toml", "App.Migrations.csproj"]);

        Assert.Equal<string>(["App.Migrations.csproj", "mist.toml"], Assert.IsType<ReferenceAmbiguous>(result).Candidates);
    }

    [Fact]
    public void ANameBelowASubdirectory_IsNeverACandidate()
    {
        string[] listing = ["App.Migrations.csproj", "bin/Debug/net10.0/App.Migrations.dll", "obj\\App.Migrations.dll", "out/mist.toml"];

        var classified = Assert.IsType<ReferenceClassified>(ReferenceClassifier.Classify(Folder, isDirectory: true, listing));

        Assert.Equal(ReferenceKind.CSharpProject, classified.Kind);
        Assert.Equal(Path.Combine(Folder, "App.Migrations.csproj"), classified.ResolvedPath);
    }

    [Fact]
    public void ACandidateExtension_IsMatchedWithoutRegardToCase()
    {
        var classified = Assert.IsType<ReferenceClassified>(
            ReferenceClassifier.Classify(Folder, isDirectory: true, ["App.Migrations.CSPROJ", "notes.txt"]));

        Assert.Equal(ReferenceKind.CSharpProject, classified.Kind);
        Assert.Equal(Path.Combine(Folder, "App.Migrations.CSPROJ"), classified.ResolvedPath);
    }
}