using Mist.Driver.Contracts;

namespace Mist.CLI.Tests;

[Trait("Tier", "1")]
public sealed class ArtifactLaunchTests
{
    private static readonly string Artifact = Path.GetFullPath(Path.Combine("repo", "App", "bin", "App.dll"));

    [Fact]
    public void AProgramReferencingAspNetCoreIsGivenTheArtifactFolderAsItsContentRoot()
    {
        var start = ArtifactLaunch.StartInfo(Artifact, [new("Microsoft.AspNetCore.App")]);

        Assert.Equal("dotnet", start.FileName);
        Assert.Equal([Artifact, "--contentRoot", Path.GetDirectoryName(Artifact)!], start.ArgumentList);
        Assert.Equal("", start.WorkingDirectory);
    }

    [Fact]
    public void AConsoleProgramIsStartedWithNoArguments()
    {
        var start = ArtifactLaunch.StartInfo(Artifact, []);

        Assert.Equal([Artifact], start.ArgumentList);
        Assert.Equal("", start.WorkingDirectory);
    }

    [Fact]
    public void AnotherFrameworkReferenceDoesNotSetTheContentRoot()
    {
        var start = ArtifactLaunch.StartInfo(Artifact, [new FrameworkReference("Microsoft.WindowsDesktop.App")]);

        Assert.Equal([Artifact], start.ArgumentList);
    }
}