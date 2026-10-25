using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

[Trait("Tier", "1")]
public sealed class LoadingDetailsTests
{
    private static readonly MistCompilation Empty = new([]);

    [Fact]
    public void ACompilationWithNoExternalReference_ReportsItsFilesAlone()
    {
        Assert.Equal(
            ["0 stdlib files", "0 referenced project files", "0 user files"],
            CompilerDriver.LoadingDetails(Empty, builtCSharpProjects: 0, externalAssemblies: 0));
    }

    [Fact]
    public void ACompilationWithExternalReferences_StatesTheProjectsBuiltAndTheAssembliesReferenced()
    {
        var details = CompilerDriver.LoadingDetails(Empty, builtCSharpProjects: 1, externalAssemblies: 2);

        Assert.Equal(["1 C# projects built", "2 external assemblies"], details.Skip(3));
    }

    [Fact]
    public void AnAssemblyReferenceAlone_StillStatesThatNoProjectWasBuilt()
    {
        var details = CompilerDriver.LoadingDetails(Empty, builtCSharpProjects: 0, externalAssemblies: 1);

        Assert.Equal(["0 C# projects built", "1 external assemblies"], details.Skip(3));
    }

    [Fact]
    public void ARuntimeIdentifier_IsStatedLast()
    {
        var details = CompilerDriver.LoadingDetails(Empty, builtCSharpProjects: 1, externalAssemblies: 1, runtimeIdentifier: "osx-arm64");

        Assert.Equal(["1 C# projects built", "1 external assemblies", "runtime identifier osx-arm64"], details.Skip(3));
    }

    [Fact]
    public void ARuntimeIdentifierWithoutExternalReferences_FollowsTheFiles()
    {
        var details = CompilerDriver.LoadingDetails(Empty, builtCSharpProjects: 0, externalAssemblies: 0, runtimeIdentifier: "linux-x64");

        Assert.Equal(["runtime identifier linux-x64"], details.Skip(3));
    }
}