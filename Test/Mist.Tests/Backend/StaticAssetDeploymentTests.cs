using System.Collections.Immutable;
using Mist.Backends.Contracts;
using Mist.Backends.CSharpTranspiler;
using Mist.Common.Source;
using Mist.Driver.Contracts;

namespace Mist.Tests.Backend;

[Trait("Tier", "1")]
public sealed class StaticAssetDeploymentTests
{
    private const string Pack = StaticAssetDeployment.FrameworkAssetsPack;
    private const string Bin = "/repo/App/bin";
    private const string Project = "/repo/App";
    private const string PackDirectory = "/nuget/microsoft.aspnetcore.app.internal.assets/10.0.9";

    private static readonly string WebRoot = Path.Combine(Project, "wwwroot");
    private static readonly ImmutableArray<FrameworkReference> AspNetCore = [new("Microsoft.AspNetCore.App")];
    private static readonly DateTime Earlier = new(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Later = new(2026, 10, 2, 0, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AnExecutableWithComponentFilesAndTheAspNetCoreReferenceServesFrameworkAssets()
    {
        Assert.True(StaticAssetDeployment.ServesFrameworkAssets(ProjectOutputType.Executable, AspNetCore, () => true));
    }

    [Fact]
    public void ALibraryDoesNotServeFrameworkAssets()
    {
        Assert.False(StaticAssetDeployment.ServesFrameworkAssets(ProjectOutputType.Library, AspNetCore, () => true));
    }

    [Fact]
    public void AProjectWithoutComponentFilesDoesNotServeFrameworkAssets()
    {
        Assert.False(StaticAssetDeployment.ServesFrameworkAssets(ProjectOutputType.Executable, AspNetCore, () => false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Microsoft.WindowsDesktop.App")]
    [InlineData("microsoft.aspnetcore.app")]
    public void OnlyTheAspNetCoreReferenceServesFrameworkAssets(string? framework)
    {
        ImmutableArray<FrameworkReference> references = framework is null ? [] : [new(framework)];

        Assert.False(StaticAssetDeployment.ServesFrameworkAssets(ProjectOutputType.Executable, references, () => true));
    }

    [Fact]
    public void TheProjectFolderIsNotSearchedForAConsoleProgram()
    {
        Assert.False(StaticAssetDeployment.ServesFrameworkAssets(
            ProjectOutputType.Executable, [], () => throw new InvalidOperationException("searched")));
    }

    [Theory]
    [InlineData("/repo/App/Components/Counter.razor", true)]
    [InlineData("/repo/App/Counter.RAZOR", true)]
    [InlineData("/repo/App/Components/Counter.probe", false)]
    public void AComponentFileIsAClaimedRazorInputOfTheRootProject(string path, bool expected)
    {
        Assert.Equal(expected, StaticAssetDeployment.HasComponentFiles([Claimed(path, Project)], Project));
    }

    [Fact]
    public void AReferencedProjectsComponentFilesDoNotCount()
    {
        Assert.False(StaticAssetDeployment.HasComponentFiles([Claimed("/repo/Lib/Widget.razor", "/repo/Lib")], Project));
    }

    [Fact]
    public void NoClaimedInputsMeansNoComponentFiles()
    {
        Assert.False(StaticAssetDeployment.HasComponentFiles([], Project));
    }

    [Fact]
    public void ThePackIsInferredAtTheTargetingPackVersion()
    {
        var packages = StaticAssetDeployment.WithFrameworkAssetsPack([new("Serilog", "4.0.0")], true, () => "10.0.9");

        Assert.Equal([new NuGetPackageReference("Serilog", "4.0.0"), new NuGetPackageReference(Pack, "10.0.9")], packages.AsEnumerable());
    }

    [Fact]
    public void ThePackIsInferredWhenNoPackagesAreDeclared()
    {
        var packages = StaticAssetDeployment.WithFrameworkAssetsPack(default, true, () => "10.0.9");

        Assert.Equal([new NuGetPackageReference(Pack, "10.0.9")], packages.AsEnumerable());
    }

    [Fact]
    public void APackagesEntryPinsThePackVersion()
    {
        ImmutableArray<NuGetPackageReference> declared = [new("microsoft.aspnetcore.app.internal.assets", "10.0.5")];
        var asked = false;

        var packages = StaticAssetDeployment.WithFrameworkAssetsPack(declared, true, () =>
        {
            asked = true;
            return "10.0.9";
        });

        Assert.Equal(declared.AsEnumerable(), packages.AsEnumerable());
        Assert.False(asked);
    }

    [Fact]
    public void NothingIsInferredForAProjectThatDoesNotServeFrameworkAssets()
    {
        ImmutableArray<NuGetPackageReference> declared = [new("Serilog", "4.0.0")];

        Assert.Equal(declared.AsEnumerable(), StaticAssetDeployment.WithFrameworkAssetsPack(declared, false, () => "10.0.9").AsEnumerable());
    }

    [Fact]
    public void NothingIsInferredWhenNoTargetingPackResolved()
    {
        ImmutableArray<NuGetPackageReference> declared = [new("Serilog", "4.0.0")];

        Assert.Equal(declared.AsEnumerable(), StaticAssetDeployment.WithFrameworkAssetsPack(declared, true, () => null).AsEnumerable());
    }

    [Fact]
    public void ThePackDirectoryIsReadOnlyWhenTheProjectServesFrameworkAssets()
    {
        var packages = new PackageResolutionResult([], [], 0)
        {
            PackageDirectories = ImmutableDictionary<string, string>.Empty.Add(Pack, PackDirectory),
        };

        Assert.Equal(PackDirectory, StaticAssetDeployment.FrameworkAssetsDirectory(true, packages));
        Assert.Null(StaticAssetDeployment.FrameworkAssetsDirectory(false, packages));
    }

    [Fact]
    public void ThePacksFrameworkFolderIsDeployedUnderTheWebRoot()
    {
        var copies = StaticAssetDeployment.Select(Bin, WebRoot, [], PackDirectory,
        [
            "_framework/blazor.web.js",
            "_framework/blazor.server.js",
            "build/Microsoft.AspNetCore.App.Internal.Assets.targets",
            "Icon.png",
            "microsoft.aspnetcore.app.internal.assets.nuspec",
        ]);

        Assert.Equal(
        [
            new FileCopy(Path.Combine(PackDirectory, "_framework/blazor.web.js"), Path.Combine(Bin, "wwwroot", "_framework", "blazor.web.js")),
            new FileCopy(Path.Combine(PackDirectory, "_framework/blazor.server.js"), Path.Combine(Bin, "wwwroot", "_framework", "blazor.server.js")),
        ], copies.AsEnumerable());
    }

    [Fact]
    public void AFileNamedLikeTheFrameworkFolderIsNotAFrameworkAsset()
    {
        Assert.Empty(StaticAssetDeployment.Select(Bin, WebRoot, [], PackDirectory, ["_framework", "lib/_framework/x.js"]));
    }

    [Fact]
    public void TheProjectsWebRootIsDeployedWithItsSubfolders()
    {
        var copies = StaticAssetDeployment.Select(Bin, WebRoot, ["app.css", "img\\logo.png"], null, []);

        Assert.Equal(
        [
            new FileCopy(Path.Combine(WebRoot, "app.css"), Path.Combine(Bin, "wwwroot", "app.css")),
            new FileCopy(Path.Combine(WebRoot, "img\\logo.png"), Path.Combine(Bin, "wwwroot", "img", "logo.png")),
        ], copies.AsEnumerable());
    }

    [Fact]
    public void TheProjectsOwnFileWinsOverThePacksFileOfTheSamePath()
    {
        var copies = StaticAssetDeployment.Select(Bin, WebRoot, ["_framework/blazor.web.js"], PackDirectory, ["_framework/blazor.web.js"]);

        Assert.Equal([new FileCopy(Path.Combine(WebRoot, "_framework/blazor.web.js"), Path.Combine(Bin, "wwwroot", "_framework", "blazor.web.js"))], copies.AsEnumerable());
    }

    [Fact]
    public void APlanCopiesOnlyWhatIsMissingOrStale()
    {
        var files = new StubFiles();
        files.Listings[WebRoot] = ["fresh.css", "stale.css", "new.css"];
        files.Listings[PackDirectory] = ["_framework/blazor.web.js"];
        files.Times[Path.Combine(WebRoot, "fresh.css")] = Earlier;
        files.Times[Path.Combine(Bin, "wwwroot", "fresh.css")] = Later;
        files.Times[Path.Combine(WebRoot, "stale.css")] = Later;
        files.Times[Path.Combine(Bin, "wwwroot", "stale.css")] = Earlier;
        files.Times[Path.Combine(WebRoot, "new.css")] = Earlier;
        files.Times[Path.Combine(PackDirectory, "_framework/blazor.web.js")] = Earlier;

        var plan = StaticAssetDeployment.Plan(Bin, Project, PackDirectory, files);

        Assert.Equal(
        [
            Path.Combine(Bin, "wwwroot", "stale.css"),
            Path.Combine(Bin, "wwwroot", "new.css"),
            Path.Combine(Bin, "wwwroot", "_framework", "blazor.web.js"),
        ], plan.Select(copy => copy.Destination));
    }

    [Fact]
    public void APlanWithoutAProjectFolderReadsNoWebRoot()
    {
        var files = new StubFiles();
        files.Listings["wwwroot"] = ["app.css"];

        Assert.Empty(StaticAssetDeployment.Plan(Bin, "", null, files));
    }

    private static ClaimedInput Claimed(string path, string projectFolder)
    {
        return new ClaimedInput(new SourceText(path, ""), new MistProject("App", projectFolder, Path.Combine(projectFolder, "mist.toml")));
    }

    private sealed class StubFiles : IDeploymentFileSource
    {
        public Dictionary<string, DateTime> Times { get; } = [];

        public Dictionary<string, ImmutableArray<string>> Listings { get; } = [];

        public bool Exists(string path)
        {
            return Times.ContainsKey(path);
        }

        public DateTime LastWriteTimeUtc(string path)
        {
            return Times[path];
        }

        public ImmutableArray<string> RuntimesListing(string outputDirectory)
        {
            return [];
        }

        public ImmutableArray<string> Listing(string directory)
        {
            return Listings.GetValueOrDefault(directory, []);
        }
    }
}