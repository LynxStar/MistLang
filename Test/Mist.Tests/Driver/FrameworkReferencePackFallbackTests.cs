using Mist.Driver;

namespace Mist.Tests.Driver;

[Trait("Tier", "2")]
public sealed class FrameworkReferencePackFallbackTests
{
    [Fact]
    public void ResolvePack_SkipsEmptyHigherMatchingVersion()
    {
        const string frameworkName = "Microsoft.AspNetCore.App";
        const string targetFramework = "net10.0";
        var dotnetRoot = Directory.CreateTempSubdirectory("mist-fwref-fallback-");
        try
        {
            var packDir = Path.Combine(dotnetRoot.FullName, "packs", "Microsoft.AspNetCore.App.Ref");
            var lowerRefDir = Path.Combine(packDir, "10.0.2", "ref", targetFramework);
            var expectedAssembly = Path.Combine(lowerRefDir, "Microsoft.AspNetCore.Http.dll");
            _ = Directory.CreateDirectory(lowerRefDir);
            File.WriteAllText(expectedAssembly, string.Empty);
            _ = Directory.CreateDirectory(Path.Combine(packDir, "10.0.9", "ref", targetFramework));

            var result = FrameworkReferenceResolver.ResolvePack(dotnetRoot.FullName, frameworkName, targetFramework);

            _ = Assert.NotNull(result);
            Assert.Equal("10.0.2", result.Value.Version);
            Assert.Equal(expectedAssembly, Assert.Single(result.Value.Assemblies));
        }
        finally
        {
            dotnetRoot.Delete(recursive: true);
        }
    }
}