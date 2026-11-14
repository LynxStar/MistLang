using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Driver;

namespace Mist.Tests.Driver;

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

    private sealed record SdkVersionEntry(int Major, int Minor, int Patch, bool HasRefAssemblies);

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

    private static Gen<List<SdkVersionEntry>> GenSdkVersionListWithCompatible(int tfmMajor)
    {
        return Gen.Choose(1, 20).SelectMany(count =>
            Gen.ArrayOf(GenSdkVersionEntry(tfmMajor), count).SelectMany(entries =>
            {
                var list = entries.ToList();
                var hasCompatible = list.Any(e => e.HasRefAssemblies);
                if(!hasCompatible)
                {
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

    private static Gen<List<SdkVersionEntry>> GenSdkVersionListAllIncompatible(int tfmMajor)
    {
        return Gen.Choose(1, 10).SelectMany(count =>
            Gen.ArrayOf(GenSdkVersionEntry(tfmMajor), count).Select(entries =>
                entries.Select(e => e with { HasRefAssemblies = false }).ToList()));
    }

    private string CreateSdkDirectoryStructure(List<SdkVersionEntry> entries, string tfm)
    {
        var sdkRoot = Path.Combine(Path.GetTempPath(), $"mist_pbt_sdk_{Guid.NewGuid():N}");
        tempDirs.Add(sdkRoot);

        var packsDir = Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref");
        _ = Directory.CreateDirectory(packsDir);

        foreach(var entry in entries)
        {
            var versionStr = $"{entry.Major}.{entry.Minor}.{entry.Patch}";
            var versionDir = Path.Combine(packsDir, versionStr);
            _ = Directory.CreateDirectory(versionDir);

            if(entry.HasRefAssemblies)
            {
                var refDir = Path.Combine(versionDir, "ref", tfm);
                _ = Directory.CreateDirectory(refDir);
            }
        }

        return sdkRoot;
    }

    private static Version? ExpectedHighestVersion(List<SdkVersionEntry> entries)
    {
        return entries
            .Where(e => e.HasRefAssemblies)
            .Select(e => new Version(e.Major, e.Minor, e.Patch))
            .OrderDescending()
            .FirstOrDefault();
    }

    [Property(MaxTest = 100)]
    public Property HighestCompatibleVersion_IsAlwaysSelected()
    {
        const int tfmMajor = 10;
        const string tfm = "net10.0";

        return Prop.ForAll(GenSdkVersionListWithCompatible(tfmMajor).ToArbitrary(), entries =>
        {
            var sdkRoot = CreateSdkDirectoryStructure(entries, tfm);
            var result = SdkDiscovery.FindHighestVersionRefPath(sdkRoot, tfm);

            if(result is null)
            {
                return false.Label("Expected non-null result but got null");
            }

            if(!result.EndsWith(Path.Combine("ref", tfm), StringComparison.Ordinal))
            {
                return false.Label($"Result path does not end with ref/{tfm}: {result}");
            }

            var relativePath = Path.GetRelativePath(sdkRoot, result);
            var parts = relativePath.Split(Path.DirectorySeparatorChar);
            // parts: ["packs", "Microsoft.NETCore.App.Ref", "{version}", "ref", "{tfm}"]
            var selectedVersionStr = parts[2];

            if(!Version.TryParse(selectedVersionStr, out var selectedVersion))
            {
                return false.Label($"Could not parse selected version: {selectedVersionStr}");
            }

            var expected = ExpectedHighestVersion(entries);
            if(expected is null)
            {
                return false.Label("Expected version is null despite guaranteed compatible entry");
            }

            return (selectedVersion == expected)
                .Label($"Expected {expected} but got {selectedVersion}");
        });
    }

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

            var expectedHighest = lines
                .Select(l => l.Version)
                .OrderDescending()
                .First();

            if(result is null)
            {
                return false.Label("ParseSdkListOutput returned null for valid input");
            }

            // Every line names the same [path], so the expected root is that path's parent.
            var expectedRoot = Path.GetDirectoryName("/usr/share/dotnet/sdk");
            return (result == expectedRoot)
                .Label($"Expected '{expectedRoot}' but got '{result}'");
        });
    }
}