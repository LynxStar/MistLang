using System.ComponentModel;
using System.Text.Json;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// The resolver's handling of what its restore left behind. The restore is a stub that writes, or fails to write,
/// the <c>obj/project.assets.json</c> a <c>dotnet restore</c> of the generated project would, and package files live
/// under a scratch folder the assets file names; the throwing stubs stand in for I/O and launch failures the
/// temporary restore project meets, which have no seam of their own.
/// </summary>
[Trait("Tier", "2")]
public sealed class PackageRestoreOutcomeTests : IDisposable
{
    private const string Feed = "https://api.nuget.org/v3/index.json";

    private readonly string scratch = Path.Combine(Path.GetTempPath(), $"mist-restore-outcome-{Guid.NewGuid():N}");

    private string? restoreDirectory;

    public void Dispose()
    {
        if(Directory.Exists(scratch))
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    [Fact]
    public void ARestoreThatWritesNoAssetsFile_FailsEveryPackageWithOneAssetsMissingError()
    {
        var diagnostics = new DiagnosticBag();

        var result = Resolve(diagnostics, [new("A", "1.0.0"), new("B", "2.0.0")], _ => (0, "", ""));

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNetworkError, finding.Descriptor);
        Assert.Equal("Package restore completed but project.assets.json was not generated.", finding.Message);
        Assert.Equal(
            new OptionalValue(SlotKind.Path, new PathValue(Path.Combine(restoreDirectory!, "obj", "project.assets.json"))),
            finding.Arguments[0]);
        AssertNothingResolved(result, failures: 2);
        Assert.False(Directory.Exists(restoreDirectory));
    }

    [Fact]
    public void AnIOFailureInTheTemporaryProject_FailsEveryPackageWithOneErrorNamingIt()
    {
        var diagnostics = new DiagnosticBag();

        var result = Resolve(diagnostics, [new("A", "1.0.0"), new("B", "2.0.0")], _ => throw new IOException("No space left on device"));

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNetworkError, finding.Descriptor);
        Assert.Equal("Failed to create temporary restore project: No space left on device", finding.Message);
        Assert.Equal(new OptionalValue(SlotKind.Path, new PathValue(restoreDirectory!)), finding.Arguments[1]);
        AssertNothingResolved(result, failures: 2);
        Assert.False(Directory.Exists(restoreDirectory));
    }

    [Fact]
    public void ARestoreThatCannotStart_FailsEveryPackageWithOneError()
    {
        var diagnostics = new DiagnosticBag();

        var result = Resolve(diagnostics, [new("A", "1.0.0"), new("B", "2.0.0")], _ => throw new Win32Exception("dotnet was not found"));

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNetworkError, finding.Descriptor);
        Assert.Equal("dotnet was not found", finding.Message);
        AssertNothingResolved(result, failures: 2);
        Assert.False(Directory.Exists(restoreDirectory));
    }

    [Fact]
    public void AnUnreadableAssetsFile_FailsEveryPackageWithOneAssetsReadError()
    {
        var diagnostics = new DiagnosticBag();

        var result = Resolve(diagnostics, [new("A", "1.0.0"), new("B", "2.0.0")], directory =>
        {
            WriteAssets(directory, "{ \"targets\": ");
            return (0, "", "");
        });

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNetworkError, finding.Descriptor);
        Assert.StartsWith("Failed to parse project.assets.json: ", finding.Message, StringComparison.Ordinal);
        Assert.Equal(
            new OptionalValue(SlotKind.Path, new PathValue(Path.Combine(restoreDirectory!, "obj", "project.assets.json"))),
            finding.Arguments[2]);
        AssertNothingResolved(result, failures: 2);
        Assert.False(Directory.Exists(restoreDirectory));
    }

    [Fact]
    public void AFailedRestore_IsReportedFromItsOutput_AndTheAssetsFileItLeftIsNotRead()
    {
        var root = Path.Combine(scratch, "nuget");
        _ = Touch(root, "a", "1.0.0", "lib", "net8.0", "A.dll");
        var diagnostics = new DiagnosticBag();

        var result = Resolve(diagnostics, [new("A", "1.0.0")], directory =>
        {
            WriteAssets(directory, Assets(root, """{ "A/1.0.0": { "type": "package", "compile": { "lib/net8.0/A.dll": {} } } }"""));
            return (1, "error NU1101: Unable to find package A. No packages exist with this id in source(s): nuget.org", "");
        });

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNotFound, finding.Descriptor);
        Assert.Equal("NU1101", finding.Native?.Code);
        AssertNothingResolved(result, failures: 1);
    }

    [Fact]
    public void ADeclaredPackageTheRestoreDidNotList_IsNotFound_WhileTheOthersAndTheirDependenciesResolve()
    {
        var root = Path.Combine(scratch, "nuget");
        var json = Touch(root, "newtonsoft.json", "13.0.3", "lib", "net8.0", "Newtonsoft.Json.dll");
        var dependency = Touch(root, "dependency", "2.0.0", "lib", "netstandard2.0", "Dependency.dll");
        var diagnostics = new DiagnosticBag();

        var result = Resolve(diagnostics, [new("newtonsoft.json", "13.0.3"), new("Ghost", "1.0.0")], directory =>
        {
            WriteAssets(directory, Assets(root, """
                {
                  "Newtonsoft.Json/13.0.3": { "type": "package", "compile": { "lib/net8.0/Newtonsoft.Json.dll": {} } },
                  "Dependency/2.0.0": { "type": "package", "compile": { "lib/netstandard2.0/Dependency.dll": {} } }
                }
                """));
            return (0, "", "");
        });

        var finding = Assert.Single(diagnostics.Admitted);
        Assert.Same(CommonDiagnostics.PackageNotFound, finding.Descriptor);
        Assert.Null(finding.Native);
        Assert.Equal($"Package 'Ghost' version '1.0.0' was not found on any configured NuGet feed. Searched: {Feed}.", finding.Message);
        Assert.Equal(1, result.FailureCount);
        Assert.Equal<string>([json, dependency], result.AssemblyPaths);
        var info = Assert.Single(result.Resolved);
        Assert.Equal("newtonsoft.json", info.Name, ignoreCase: true);
        Assert.Equal(("13.0.3", "net8.0", 1), (info.Version, info.SelectedTfm, info.AssemblyCount));
    }

    [Fact]
    public void AnAssemblyTheFrameworkProvides_IsLeftOut_WhicheverPackageShipsIt()
    {
        var root = Path.Combine(scratch, "nuget");
        var legacy = Touch(root, "legacy", "1.0.0", "lib", "net8.0", "Legacy.dll");
        _ = Touch(root, "legacy", "1.0.0", "lib", "net8.0", "system.buffers.dll");
        _ = Touch(root, "legacy", "1.0.0", "lib", "net8.0", "System.Memory.dll");
        var mixed = Touch(root, "mixed", "1.0.0", "lib", "net8.0", "Mixed.dll");
        var diagnostics = new DiagnosticBag();

        var result = Resolve(diagnostics, [new("Legacy", "1.0.0"), new("Mixed", "1.0.0")], directory =>
        {
            WriteAssets(directory, Assets(
                root,
                """
                {
                  "System.Buffers/4.5.1": { "type": "package", "compile": { "ref/netcoreapp2.0/_._": {} } },
                  "Legacy/1.0.0": {
                    "type": "package",
                    "compile": { "lib/net8.0/Legacy.dll": {}, "lib/net8.0/system.buffers.dll": {}, "lib/net8.0/System.Memory.dll": {} }
                  },
                  "Mixed/1.0.0": { "type": "package", "compile": { "lib/net8.0/_._": {}, "lib/net8.0/Mixed.dll": {} } }
                }
                """,
                """{ "frameworkAssemblies": { "System.Memory": {} } }"""));
            return (0, "", "");
        });

        Assert.Equal<string>([legacy, mixed], result.AssemblyPaths);
        Assert.Equal<ResolvedPackageInfo>(
            [new("Legacy", "1.0.0", "net8.0", 1), new("Mixed", "1.0.0", "net8.0", 1)],
            result.Resolved);
        Assert.Empty(diagnostics.Admitted);
    }

    [Fact]
    public void TheGraphTheAssetsFileNames_OrdersTheRuntimeAssets_AndIsReturned()
    {
        var root = Path.Combine(scratch, "nuget");
        _ = Touch(root, "native", "1.0.0", "lib", "net8.0", "Native.dll");
        var graphPath = Path.Combine(scratch, "RuntimeIdentifierGraph.json");
        File.WriteAllText(graphPath, """{ "runtimes": { "probe-rid": { "#import": ["probe-base"] }, "probe-base": { "#import": [] } } }""");

        var result = Resolve(new DiagnosticBag(), [new("Native", "1.0.0")], directory =>
        {
            WriteAssets(directory, Assets(
                root,
                """
                {
                  "Native/1.0.0": {
                    "type": "package",
                    "compile": { "lib/net8.0/Native.dll": {} },
                    "runtimeTargets": {
                      "runtimes/probe-base/native/libnative.so": { "assetType": "native", "rid": "probe-base" },
                      "runtimes/any/native/libfallback.so": { "assetType": "native", "rid": "any" }
                    }
                  }
                }
                """,
                $$"""{ "runtimeIdentifierGraphPath": {{JsonSerializer.Serialize(graphPath)}} }"""));
            return (0, "", "");
        }, runtimeIdentifier: "probe-rid");

        var asset = Assert.Single(result.RuntimeAssets);
        Assert.Equal(Path.Combine(root, "native", "1.0.0", "runtimes", "probe-base", "native", "libnative.so"), asset.Path);
        Assert.NotNull(result.RuntimeIdentifierGraph);
        Assert.True(RidGraph.Contains(result.RuntimeIdentifierGraph, "probe-rid"));
        Assert.False(RidGraph.Contains(result.RuntimeIdentifierGraph, "linux-x64"));
    }

    private static void AssertNothingResolved(PackageResolutionResult result, int failures)
    {
        Assert.Equal(failures, result.FailureCount);
        Assert.Empty(result.AssemblyPaths);
        Assert.Empty(result.Resolved);
    }

    private static string Assets(string packageFolder, string libraries, string framework = "{}")
    {
        var folder = JsonSerializer.Serialize(packageFolder);
        return $$"""
            {
              "version": 3,
              "targets": { "net10.0": {{libraries}} },
              "packageFolders": { {{folder}}: {} },
              "project": { "restore": { "packagesPath": {{folder}} }, "frameworks": { "net10.0": {{framework}} } }
            }
            """;
    }

    private static void WriteAssets(string directory, string json)
    {
        var obj = Directory.CreateDirectory(Path.Combine(directory, "obj")).FullName;
        File.WriteAllText(Path.Combine(obj, "project.assets.json"), json);
    }

    private static string Touch(string root, params string[] path)
    {
        var file = Path.Combine([root, .. path]);
        _ = Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllBytes(file, []);
        return file;
    }

    private PackageResolutionResult Resolve(
        DiagnosticBag diagnostics,
        NuGetPackageReference[] declared,
        Func<string, (int ExitCode, string Stdout, string Stderr)> restore,
        string? runtimeIdentifier = null)
    {
        return PackageResolver.ResolvePackages(
            [.. declared],
            [],
            "net10.0",
            diagnostics,
            null,
            (_, directory) =>
            {
                restoreDirectory = directory;
                return restore(directory);
            },
            runtimeIdentifier);
    }
}