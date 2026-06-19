// Feature: csharp-interop-v2, Task 8.4
// In-process tests for restore logic — invokes PackageResolver.ResolvePackages
// and FrameworkReferenceResolver.Resolve directly (no process spawning) and
// verifies the formatted output, exit codes, and diagnostics.
//
// Validates: Requirements 10.2, 13.2, 13.5

using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// In-process tests for the restore command logic. Rather than spawning the CLI,
/// these tests call <see cref="PackageResolver.ResolvePackages"/> and
/// <see cref="FrameworkReferenceResolver.Resolve"/> directly, then verify the
/// output formatting and exit-code-equivalent logic (FailureCount == 0 → exit 0).
/// </summary>
public sealed class RestoreCommandInProcessTests : IDisposable
{
    private const string TargetFramework = "net10.0";

    private static readonly string GlobalPackagesFolder =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".nuget",
            "packages");

    private readonly List<string> _createdPackageRoots = [];

    /// <summary>
    /// Creates a synthetic cached package under the global packages folder.
    /// </summary>
    private (string Name, string Version) CreateCachedPackage(string? libTfm)
    {
        var name = $"mist.test.restore.{Guid.NewGuid():N}";
        const string version = "2.1.0";

        var packageRoot = Path.Combine(GlobalPackagesFolder, name);
        var versionDir = Path.Combine(packageRoot, version);
        Directory.CreateDirectory(versionDir);
        _createdPackageRoots.Add(packageRoot);

        if (libTfm is not null)
        {
            var tfmDir = Path.Combine(versionDir, "lib", libTfm);
            Directory.CreateDirectory(tfmDir);
            File.WriteAllText(Path.Combine(tfmDir, "Placeholder.dll"), string.Empty);
        }

        return (name, version);
    }

    public void Dispose()
    {
        foreach (var root in _createdPackageRoots)
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch (IOException) { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// Simulates the RunRestoreCommand output formatting for a given resolution result,
    /// matching the real CLI's logic from Program.cs.
    /// </summary>
    private static (int ExitCode, string Output, string ErrorOutput) SimulateRestoreOutput(
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences)
    {
        var packageDiagnostics = new DiagnosticBag();
        var packageResult = PackageResolver.ResolvePackages(packages, TargetFramework, packageDiagnostics);

        var frameworkDiagnostics = new DiagnosticBag();
        var frameworkResult = FrameworkReferenceResolver.Resolve(frameworkReferences, TargetFramework, frameworkDiagnostics);

        var outputLines = new List<string>();
        var errorLines = new List<string>();

        // Per-package detail
        foreach (var pkg in packageResult.Resolved)
        {
            outputLines.Add($"Restored {pkg.Name} {pkg.Version} ({pkg.SelectedTfm})");
        }

        // Per-framework detail
        foreach (var fw in frameworkResult.Resolved)
        {
            outputLines.Add($"Resolved {fw.Name} {fw.Version}");
        }

        // Zero-assemblies notes
        foreach (var pkg in packageResult.Resolved)
        {
            if (pkg.AssemblyCount == 0)
            {
                outputLines.Add($"{pkg.Name} contributed no assemblies for {TargetFramework}");
            }
        }

        // Summary
        var successCount = packageResult.Resolved.Length + frameworkResult.Resolved.Length;
        var failureCount = packageResult.FailureCount + frameworkResult.FailureCount;
        outputLines.Add($"{successCount} succeeded, {failureCount} failed");

        // Diagnostics on error
        foreach (var diag in packageDiagnostics.ToImmutableArray())
        {
            errorLines.Add(DiagnosticFormatter.Format(diag));
        }
        foreach (var diag in frameworkDiagnostics.ToImmutableArray())
        {
            errorLines.Add(DiagnosticFormatter.Format(diag));
        }

        var exitCode = failureCount > 0 ? 1 : 0;
        return (exitCode, string.Join("\n", outputLines), string.Join("\n", errorLines));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 1: Restore success (pre-cached package) → exit 0, output contains
    //          "Restored {name} {ver} ({tfm})", summary shows "1 succeeded, 0 failed"
    //          Validates: Requirements 10.2, 13.2
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RestoreSuccess_CachedPackage_ExitZero_OutputContainsRestoredLine()
    {
        var (name, version) = CreateCachedPackage(libTfm: "net8.0");

        var (exitCode, output, errorOutput) = SimulateRestoreOutput(
            [new NuGetPackageReference(name, version)],
            ImmutableArray<FrameworkReference>.Empty);

        Assert.Equal(0, exitCode);
        Assert.Contains($"Restored {name} {version} (net8.0)", output);
        Assert.Contains("1 succeeded, 0 failed", output);
        Assert.Empty(errorOutput);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 2: Restore failure (invalid version) → exit non-zero, MIST_PKG_002,
    //          summary shows "0 succeeded, 1 failed"
    //          Validates: Requirements 10.2, 13.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RestoreFailure_InvalidVersion_ExitNonZero_MistPkg002()
    {
        var (exitCode, output, errorOutput) = SimulateRestoreOutput(
            [new NuGetPackageReference("BadPkg", "invalid-version-string")],
            ImmutableArray<FrameworkReference>.Empty);

        Assert.Equal(1, exitCode);
        Assert.Contains("0 succeeded, 1 failed", output);
        Assert.Contains("MIST_PKG_002", errorOutput);
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Test 3: Restore mixed (one good + one bad) → "1 succeeded, 1 failed"
    //          Validates: Requirements 10.2, 13.2, 13.5
    // ═══════════════════════════════════════════════════════════════════════

    [Fact]
    public void RestoreMixed_OneGoodOneBad_ShowsMixedSummary()
    {
        var (goodName, goodVersion) = CreateCachedPackage(libTfm: "net8.0");

        var (exitCode, output, errorOutput) = SimulateRestoreOutput(
            [
                new NuGetPackageReference(goodName, goodVersion),
                new NuGetPackageReference("NonExistentPkg", "bad-version"),
            ],
            ImmutableArray<FrameworkReference>.Empty);

        Assert.Equal(1, exitCode);
        Assert.Contains($"Restored {goodName} {goodVersion} (net8.0)", output);
        Assert.Contains("1 succeeded, 1 failed", output);
        Assert.Contains("MIST_PKG_002", errorOutput);
    }
}
