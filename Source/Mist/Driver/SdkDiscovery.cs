using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;

namespace Mist.Driver;

/// <summary>
/// Caches the reference assembly path in process and on disk so a compilation need not spawn
/// <c>dotnet --list-sdks</c>.
/// </summary>
public static partial class SdkDiscovery
{
    private static readonly SdkReferencePathCacheFile CacheFile = new(
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mist",
            "sdk-ref-path.cache"));

    private static readonly SdkReferencePathCache ReferencePathCache = new(
        RunFullDiscovery,
        Directory.Exists,
        CacheFile.Read,
        CacheFile.Update);

    private const int DotnetTimeoutMs = 10_000;

    [GeneratedRegex(@"^(\d+\.\d+\.\d+)\s+\[(.+)\]$")]
    private static partial Regex SdkLinePattern();

    public static string? FindReferenceAssemblyPath(string targetFramework, DiagnosticBag diagnostics)
    {
        return ReferencePathCache.Find(targetFramework, diagnostics);
    }

    public static void RegenerateDiskCache()
    {
        _ = ReferencePathCache.Regenerate("net10.0", new DiagnosticBag());
    }

    private static string? RunFullDiscovery(string targetFramework, DiagnosticBag diagnostics)
    {
        var tfm = targetFramework;

        var sdkRoot = FindDotnetRoot();

        if(sdkRoot is null)
        {
            diagnostics.ReportTyped(CommonDiagnostics.SdkNotFound, new Absent(AbsenceReason.Unavailable));
            return null;
        }

        var refPath = FindHighestVersionRefPath(sdkRoot, tfm);
        if(refPath is null)
        {
            diagnostics.ReportTyped(CommonDiagnostics.SdkNoFramework, new Absent(AbsenceReason.Unavailable),
                new NameValue(tfm), new PathValue(sdkRoot));
            return null;
        }

        return refPath;
    }

    /// <summary>
    /// The directory holding <c>packs/</c>. Uncached: <see cref="FindReferenceAssemblyPath"/> caches the
    /// reference-assembly directory, not this root.
    /// </summary>
    internal static string? FindDotnetRoot()
    {
        return TryDiscoverViaDotnetCli() ?? TryWellKnownPaths();
    }

    private static string? TryDiscoverViaDotnetCli()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = "--list-sdks",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if(process is null)
            {
                return null;
            }

            var output = process.StandardOutput.ReadToEnd();
            if(!process.WaitForExit(DotnetTimeoutMs))
            {
                try { process.Kill(); } catch { /* best effort */ }

                return null;
            }

            if(process.ExitCode != 0)
            {
                return null;
            }

            return ParseSdkListOutput(output);
        }
        catch
        {
            // dotnet not on PATH or other failure
            return null;
        }
    }

    internal static string? ParseSdkListOutput(string output)
    {
        if(string.IsNullOrWhiteSpace(output))
        {
            return null;
        }

        Version? highestVersion = null;
        string? highestPath = null;

        foreach(var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            var match = SdkLinePattern().Match(trimmed);
            if(!match.Success)
            {
                continue;
            }

            var versionStr = match.Groups[1].Value;
            var sdkPath = match.Groups[2].Value;

            if(Version.TryParse(versionStr, out var version))
            {
                if(highestVersion is null || version > highestVersion)
                {
                    highestVersion = version;
                    highestPath = sdkPath;
                }
            }
        }

        if(highestPath is null)
        {
            return null;
        }

        // Each listed path is the sdk folder holding the SDK versions; the dotnet root is its parent.
        var dotnetRoot = Path.GetDirectoryName(highestPath);
        return dotnetRoot;
    }

    private static string? TryWellKnownPaths()
    {
        var candidates = GetWellKnownDotnetPaths();

        foreach(var candidate in candidates)
        {
            if(Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "packs")))
            {
                return candidate;
            }
        }

        return null;
    }

    private static string[] GetWellKnownDotnetPaths()
    {
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return
            [
                @"C:\Program Files\dotnet",
                Path.Combine(homePath, ".dotnet"),
            ];
        }

        if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return
            [
                "/usr/local/share/dotnet",
                "/usr/share/dotnet",
                Path.Combine(homePath, ".dotnet"),
            ];
        }

        return
        [
            "/usr/share/dotnet",
            "/usr/local/share/dotnet",
            Path.Combine(homePath, ".dotnet"),
        ];
    }

    internal static string? FindHighestVersionRefPath(string sdkRoot, string tfm)
    {
        var packsDir = Path.Combine(sdkRoot, "packs", "Microsoft.NETCore.App.Ref");
        if(!Directory.Exists(packsDir))
        {
            return null;
        }

        string[] versionDirs;
        try
        {
            versionDirs = Directory.GetDirectories(packsDir);
        }
        catch
        {
            return null;
        }

        var tfmMajor = ExtractTfmMajorVersion(tfm);

        Version? highestVersion = null;
        string? highestRefPath = null;

        foreach(var versionDir in versionDirs)
        {
            var dirName = Path.GetFileName(versionDir);
            if(!Version.TryParse(dirName, out var version))
            {
                continue;
            }

            if(tfmMajor.HasValue && version.Major != tfmMajor.Value)
            {
                continue;
            }

            var refPath = Path.Combine(versionDir, "ref", tfm);
            if(!Directory.Exists(refPath))
            {
                continue;
            }

            if(highestVersion is null || version > highestVersion)
            {
                highestVersion = version;
                highestRefPath = refPath;
            }
        }

        return highestRefPath;
    }

    internal static int? ExtractTfmMajorVersion(string tfm)
    {
        if(tfm.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tfm.Contains('.'))
        {
            var numPart = tfm.AsSpan(3); // skip "net"
            var dotIndex = numPart.IndexOf('.');
            if(dotIndex > 0 && int.TryParse(numPart[..dotIndex], out var major))
            {
                return major;
            }
        }

        return null;
    }
}