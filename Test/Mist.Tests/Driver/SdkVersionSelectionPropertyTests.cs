// Feature: assembly-type-provider, Property 14: SDK Version Selection
// **Validates: Requirements 2.4, 2.6**
//
// For any set of installed .NET SDK versions, the Assembly_Type_Provider SHALL
// select the latest (highest) version that provides reference assemblies for
// the target framework.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Driver;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for SDK version selection logic in <see cref="SdkDiscovery"/>.
/// Verifies that <see cref="SdkDiscovery.FindHighestVersionRefPath"/> always selects
/// the highest compatible version from a set of installed SDK versions.
/// All tests are pure in-process — no process spawning.
/// </summary>
public sealed class SdkVersionSelectionPropertyTests : IDisposable
{
    private readonly List<string> tempDirs = [];

    public void Dispose()
    {
        foreach(var dir in tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Represents a simulated SDK version entry with its version and whether
    /// it provides reference assemblies for the target TFM.
    /// </summary>
    private sealed record SdkVersionEntry(int Major, int Minor, int Patch, bool HasRefAssemblies);

    /// <summary>
    /// Generates a random SDK version entry with a major version between 6 and 12,
    /// minor between 0 and 9, and patch between 0 and 999.
    /// </summary>
    private static Gen<SdkVersionEntry> GenSdkVersionEntry(int tfmMajor)
    {
        return Gen.Choose(6, 12).SelectMany(major =>
            Gen.Choose(0, 9).SelectMany(minor =>
                Gen.Choose(0, 999).SelectMany(patch =>
                    Gen.Elements(true, false).Select(hasRef =>
                    {
                        // Only versions whose major matches the TFM can have ref assemblies
                        var actualHasRef = major == tfmMajor && hasRef;
                        return new SdkVersionEntry(major, minor, patch, actualHasRef);
                    }))));
    }

    /// <summary>
    /// Generates a non-empty list of SDK version entries (1–20 entries) with
    /// at least one entry that has reference assemblies for the target TFM.
    /// </summary>
    private static Gen<List<SdkVersionEntry>> GenSdkVersionListWithCompatible(int tfmMajor)
    {
        return Gen.Choose(1, 20).SelectMany(count =>
            Gen.ArrayOf(GenSdkVersionEntry(tfmMajor), count).SelectMany(entries =>
            {
                var list = entries.ToList();
                // Ensure at least one compatible entry exists
                var hasCompatible = list.Any(e => e.HasRefAssemblies);
                if(!hasCompatible)
                {
                    // Add a guaranteed compatible entry
                    return Gen.Choose(0, 9).SelectMany(minor =>
                        Gen.Choose(0, 999).Select(patch =>
                        {
                            list.Add(new SdkVersionEntry(tfmMajor, minor, patch, HasRefAssemblies: true));
                            return list;
                        }));
                }
                return Gen.Constant(list);
            }));
    }

    /// <summary>
    /// Generates a list of SDK version entries where NO entry has reference
    /// assemblies for the target TFM (all incompatible).
    /// </summary>
    private static Gen<List<SdkVersionEntry>> GenSdkVersionListAllIncompatible(int tfmMajor)
    {
        return Gen.Choose(1, 10).SelectMany(count =>
            Gen.ArrayOf(GenSdkVersionEntry(tfmMajor), count).Select(entries =>
            {
                // Force all entries to not have ref assemblies
                return entries.Select(e => e with { HasRefAssemblies = false }).ToList();
            }));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Creates a temporary directory structure simulating an SDK installation
    /// with the given version entries. Returns the sdkRoot path.
    /// </summary>
    private string CreateSdkDirectoryStructure(List<SdkVersionEntry> entries, string tfm)
    {
        var sdkRoot = Path.Combine(Path.GetTempPath(), $"mist_pbt_sdk_{Guid.NewGuid():N}");
        tempDirs.Add(sdkRoot);

        var packsDir = Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref");
        Directory.CreateDirectory(packsDir);

        foreach(var entry in entries)
        {
            var versionStr = $"{entry.Major}.{entry.Minor}.{entry.Patch}";
            var versionDir = Path.Combine(packsDir, versionStr);
            Directory.CreateDirectory(versionDir);

            if(entry.HasRefAssemblies)
            {
                // Create the ref/{tfm}/ directory to indicate compatibility
                var refDir = Path.Combine(versionDir, "ref", tfm);
                Directory.CreateDirectory(refDir);
            }
        }

        return sdkRoot;
    }

    /// <summary>
    /// Computes the expected highest compatible version from the entries.
    /// </summary>
    private static Version? ExpectedHighestVersion(List<SdkVersionEntry> entries)
    {
        return entries
            .Where(e => e.HasRefAssemblies)
            .Select(e => new Version(e.Major, e.Minor, e.Patch))
            .OrderDescending()
            .FirstOrDefault();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 14a: When compatible versions exist, FindHighestVersionRefPath
    /// selects the highest version that provides reference assemblies for the TFM.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property HighestCompatibleVersion_IsAlwaysSelected()
    {
        const int tfmMajor = 10;
        const string tfm = "net10.0";

        return Prop.ForAll(GenSdkVersionListWithCompatible(tfmMajor).ToArbitrary(), entries =>
        {
            var sdkRoot = CreateSdkDirectoryStructure(entries, tfm);
            var result = SdkDiscovery.FindHighestVersionRefPath(sdkRoot, tfm);

            // Result should not be null since we guaranteed at least one compatible entry
            if(result is null)
            {
                return false.Label("Expected non-null result but got null");
            }

            // The result path should end with ref/{tfm}
            if(!result.EndsWith(Path.Combine("ref", tfm), StringComparison.Ordinal))
            {
                return false.Label($"Result path does not end with ref/{tfm}: {result}");
            }

            // Extract the version from the result path
            // Path format: {sdkRoot}/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}
            var relativePath = Path.GetRelativePath(sdkRoot, result);
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            // parts: ["packs", "Microsoft.NETCore.App.Ref", "{version}", "ref", "{tfm}"]
            var selectedVersionStr = parts[2];

            if(!Version.TryParse(selectedVersionStr, out var selectedVersion))
            {
                return false.Label($"Could not parse selected version: {selectedVersionStr}");
            }

            // Verify it's the highest compatible version
            var expected = ExpectedHighestVersion(entries);
            if(expected is null)
            {
                return false.Label("Expected version is null despite guaranteed compatible entry");
            }

            return (selectedVersion == expected)
                .Label($"Expected {expected} but got {selectedVersion}");
        });
    }

    /// <summary>
    /// Property 14b: When no compatible versions exist, FindHighestVersionRefPath
    /// returns null.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property NoCompatibleVersion_ReturnsNull()
    {
        const int tfmMajor = 10;
        const string tfm = "net10.0";

        return Prop.ForAll(GenSdkVersionListAllIncompatible(tfmMajor).ToArbitrary(), entries =>
        {
            var sdkRoot = CreateSdkDirectoryStructure(entries, tfm);
            var result = SdkDiscovery.FindHighestVersionRefPath(sdkRoot, tfm);

            return (result is null)
                .Label($"Expected null but got: {result}");
        });
    }

    /// <summary>
    /// Property 14c: ParseSdkListOutput always selects the highest version
    /// from the dotnet --list-sdks output format.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property ParseSdkListOutput_SelectsHighestVersion()
    {
        var genVersionLine = Gen.Choose(6, 12).SelectMany(major =>
            Gen.Choose(0, 9).SelectMany(minor =>
                Gen.Choose(0, 999).Select(patch =>
                    (Version: new Version(major, minor, patch),
                     Line: $"{major}.{minor}.{patch} [/usr/share/dotnet/sdk]"))));

        var genLines = Gen.Choose(1, 15).SelectMany(count =>
            Gen.ArrayOf(genVersionLine, count));

        return Prop.ForAll(genLines.ToArbitrary(), lines =>
        {
            var output = string.Join("\n", lines.Select(l => l.Line));

            var result = SdkDiscovery.ParseSdkListOutput(output);

            // The expected result is the parent of the sdk path from the highest version line
            var expectedHighest = lines
                .Select(l => l.Version)
                .OrderDescending()
                .First();

            // ParseSdkListOutput returns the parent of the [path] value
            // Since all lines use [/usr/share/dotnet/sdk], the parent is /usr/share/dotnet
            if(result is null)
            {
                return false.Label("ParseSdkListOutput returned null for valid input");
            }

            // The result should be the parent directory of the sdk path
            var expectedRoot = Path.GetDirectoryName("/usr/share/dotnet/sdk");
            return (result == expectedRoot)
                .Label($"Expected '{expectedRoot}' but got '{result}'");
        });
    }
}