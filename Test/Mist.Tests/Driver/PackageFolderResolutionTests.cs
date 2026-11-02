using System.Text.Json;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// The resolver reading a successful restore's assets file: the restore is a stub that writes a hand-built
/// <c>project.assets.json</c> naming package folders under a scratch root, which alone holds the package files.
/// </summary>
[Trait("Tier", "2")]
public sealed class PackageFolderResolutionTests : IDisposable
{
    private readonly string scratch = Path.Combine(Path.GetTempPath(), $"mist-package-folders-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if(Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void AnIsolatedPackageFolder_SuppliesTheDeclaredPackagesCompileAssembly()
    {
        var root = Path.Combine(scratch, "nuget") + Path.DirectorySeparatorChar;
        var dll = Touch(root, "probe.package", "1.0.0", "lib", "net8.0", "Probe.Package.dll");
        var diagnostics = new DiagnosticBag();

        var result = Resolve(
            diagnostics,
            [root],
            """{ "Probe.Package/1.0.0": { "type": "package", "compile": { "lib/net8.0/Probe.Package.dll": {} } } }""");

        Assert.Equal<string>([dll], result.AssemblyPaths);
        Assert.StartsWith(root, result.AssemblyPaths[0], StringComparison.Ordinal);
        var info = Assert.Single(result.Resolved);
        Assert.Equal(new ResolvedPackageInfo("Probe.Package", "1.0.0", "net8.0", 1), info);
        Assert.Equal(0, result.FailureCount);
        Assert.Empty(diagnostics.ToImmutableArray());
    }

    [Fact]
    public void APackageWithOnlyRuntimeAssets_FallsBackToThemUnderTheIsolatedFolder()
    {
        var root = Path.Combine(scratch, "nuget");
        var dll = Touch(root, "runtime.only", "2.0.0", "lib", "net8.0", "Runtime.Only.dll");

        var result = Resolve(
            new DiagnosticBag(),
            [root],
            """{ "Runtime.Only/2.0.0": { "type": "package", "runtime": { "lib/net8.0/Runtime.Only.dll": {} } } }""");

        Assert.Equal<string>([dll], result.AssemblyPaths);
        Assert.Equal(1, Assert.Single(result.Resolved).AssemblyCount);
    }

    [Fact]
    public void RuntimeTargets_AreSelectedUnderTheIsolatedFolder()
    {
        var root = Path.Combine(scratch, "nuget");
        _ = Touch(root, "native.package", "3.0.0", "lib", "net8.0", "Native.Package.dll");

        var result = Resolve(
            new DiagnosticBag(),
            [root],
            """
            {
              "Native.Package/3.0.0": {
                "type": "package",
                "compile": { "lib/net8.0/Native.Package.dll": {} },
                "runtimeTargets": { "runtimes/linux-x64/native/libnative.so": { "assetType": "native", "rid": "linux-x64" } }
              }
            }
            """,
            runtimeIdentifier: "linux-x64");

        var asset = Assert.Single(result.RuntimeAssets);
        Assert.Equal(Path.Combine(root, "native.package", "3.0.0", "runtimes", "linux-x64", "native", "libnative.so"), asset.Path);
    }

    [Fact]
    public void APackageFoundInAFallbackFolder_IsReadFromThatFolder()
    {
        var global = Path.Combine(scratch, "global");
        var fallback = Path.Combine(scratch, "fallback");
        _ = Directory.CreateDirectory(global);
        var dll = Touch(fallback, "probe.package", "1.0.0", "lib", "net8.0", "Probe.Package.dll");

        var result = Resolve(
            new DiagnosticBag(),
            [global, fallback],
            """{ "Probe.Package/1.0.0": { "type": "package", "compile": { "lib/net8.0/Probe.Package.dll": {} } } }""");

        Assert.Equal<string>([dll], result.AssemblyPaths);
    }

    private static string Touch(string root, params string[] path)
    {
        var file = Path.Combine([root, .. path]);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, []);
        return file;
    }

    private static PackageResolutionResult Resolve(
        DiagnosticBag diagnostics,
        string[] packageFolders,
        string libraries,
        string? runtimeIdentifier = null)
    {
        using var document = JsonDocument.Parse(libraries);
        var declared = document.RootElement.EnumerateObject()
            .Select(library => library.Name.Split('/'))
            .Select(parts => new NuGetPackageReference(parts[0], parts[1]))
            .ToArray();
        var folders = string.Join(", ", packageFolders.Select(folder => $"{JsonSerializer.Serialize(folder)}: {{}}"));

        return PackageResolver.ResolvePackages(
            [.. declared],
            [],
            "net10.0",
            diagnostics,
            null,
            (_, restoreDirectory) =>
            {
                var obj = Directory.CreateDirectory(Path.Combine(restoreDirectory, "obj")).FullName;
                File.WriteAllText(
                    Path.Combine(obj, "project.assets.json"),
                    $$"""
                    {
                      "version": 3,
                      "targets": { "net10.0": {{libraries}} },
                      "packageFolders": { {{folders}} },
                      "project": { "restore": { "packagesPath": {{JsonSerializer.Serialize(packageFolders[0])}} } }
                    }
                    """);
                return (0, "", "");
            },
            runtimeIdentifier);
    }
}