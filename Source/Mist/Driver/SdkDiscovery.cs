using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Mist.Common.Diagnostics;

namespace Mist.Driver;

/// <summary>
/// Locates the .NET SDK installation and resolves the target framework
/// reference assembly path. Implements a two-level cache (in-process static
/// + disk file) to avoid spawning <c>dotnet --list-sdks</c> on every compilation.
/// </summary>
public static partial class SdkDiscovery
{
    private static readonly string CacheFilePath =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".mist",
            "sdk-ref-path.cache");

    // In-process cache: avoids even the disk read on repeated calls
    // within the same process lifetime.
    private static string? inProcessCache;
    private static bool inProcessCacheInitialized;
    private static readonly object cacheLock = new();

    /// <summary>
    /// Timeout for the <c>dotnet --list-sdks</c> process spawn.
    /// </summary>
    private const int DotnetTimeoutMs = 10_000;

    [GeneratedRegex(@"^(\d+\.\d+\.\d+)\s+\[(.+)\]$")]
    private static partial Regex SdkLinePattern();

    /// <summary>
    /// Discovers the SDK reference assembly path for the given target framework.
    /// Uses a two-level cache: in-process static cache, then disk cache at
    /// <c>~/.mist/sdk-ref-path.cache</c>, then full discovery via
    /// <c>dotnet --list-sdks</c> and well-known paths.
    /// Returns <c>null</c> and emits diagnostics on failure.
    /// </summary>
    public static string? FindReferenceAssemblyPath(string targetFramework, DiagnosticBag diagnostics)
    {
        // 1. In-process cache (instant)
        lock(cacheLock)
        {
            if(inProcessCacheInitialized)
            {
                if(inProcessCache is not null && Directory.Exists(inProcessCache))
                {
                    return inProcessCache;
                }

                // Cached value is stale — fall through to rediscovery.
                inProcessCacheInitialized = false;
                inProcessCache = null;
            }
        }

        // 2. Disk cache (file read, validated by Directory.Exists)
        var diskCached = ReadDiskCache();
        if(diskCached is not null && Directory.Exists(diskCached))
        {
            lock(cacheLock)
            {
                inProcessCache = diskCached;
                inProcessCacheInitialized = true;
            }
            return diskCached;
        }

        // 3. Full discovery
        var discovered = RunFullDiscovery(targetFramework, diagnostics);
        if(discovered is not null)
        {
            lock(cacheLock)
            {
                inProcessCache = discovered;
                inProcessCacheInitialized = true;
            }
            WriteDiskCache(discovered);
        }

        return discovered;
    }

    /// <summary>
    /// Regenerates the disk cache by running full SDK discovery.
    /// Called by the publish script to warm the cache after SDK updates.
    /// </summary>
    public static void RegenerateDiskCache()
    {
        // Clear in-process cache so full discovery runs.
        lock(cacheLock)
        {
            inProcessCache = null;
            inProcessCacheInitialized = false;
        }

        var diagnostics = new DiagnosticBag();
        var path = RunFullDiscovery("net10.0", diagnostics);
        if(path is not null)
        {
            WriteDiskCache(path);
            lock(cacheLock)
            {
                inProcessCache = path;
                inProcessCacheInitialized = true;
            }
        }
    }

    /// <summary>
    /// Resets the in-process cache. Intended for testing only.
    /// </summary>
    internal static void ResetInProcessCache()
    {
        lock(cacheLock)
        {
            inProcessCache = null;
            inProcessCacheInitialized = false;
        }
    }

    private static string? RunFullDiscovery(string targetFramework, DiagnosticBag diagnostics)
    {
        // Extract the TFM short name (e.g., "net10.0" → "net10.0")
        var tfm = targetFramework;

        // Locate the dotnet root (the directory containing packs/).
        var sdkRoot = FindDotnetRoot();

        if(sdkRoot is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.SdkNotFound,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                "The .NET SDK installation could not be located. Searched: 'dotnet --list-sdks' and well-known paths (/usr/share/dotnet, C:\\Program Files\\dotnet, ~/.dotnet).",
                Location: default,
                CompilationUnitPath: "<sdk-discovery>",
                AnalyzerName: null));
            return null;
        }

        // Find the highest version reference assembly path for the target framework.
        var refPath = FindHighestVersionRefPath(sdkRoot, tfm);
        if(refPath is null)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.SdkNoFramework,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"The target framework '{tfm}' reference assemblies were not found in the SDK at '{sdkRoot}'.",
                Location: default,
                CompilationUnitPath: "<sdk-discovery>",
                AnalyzerName: null));
            return null;
        }

        return refPath;
    }

    /// <summary>
    /// Discovers the .NET installation root: the directory that contains the
    /// <c>packs/</c> folder (e.g., <c>/usr/share/dotnet</c>). Tries
    /// <c>dotnet --list-sdks</c> first, then falls back to well-known paths.
    /// Returns <c>null</c> when no installation can be located.
    /// </summary>
    /// <remarks>
    /// Exposed for reuse by <see cref="FrameworkReferenceResolver"/>, which
    /// resolves reference packs relative to the same root. This method performs
    /// no caching; the cached path served by
    /// <see cref="FindReferenceAssemblyPath"/> is the resolved reference-assembly
    /// directory, not the dotnet root.
    /// </remarks>
    internal static string? FindDotnetRoot()
    {
        // Try dotnet --list-sdks first, then fall back to well-known paths.
        return TryDiscoverViaDotnetCli() ?? TryWellKnownPaths();
    }

    /// <summary>
    /// Runs <c>dotnet --list-sdks</c> and parses the output to find the SDK root.
    /// Returns the dotnet root directory (parent of the sdk/ folder), or null on failure.
    /// </summary>
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

    /// <summary>
    /// Parses the output of <c>dotnet --list-sdks</c> to find the SDK root directory.
    /// Each line has the format: <c>version [path]</c>
    /// We pick the highest version and derive the dotnet root from the path.
    /// </summary>
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
                    // The path in the output is the sdk folder (e.g., /usr/share/dotnet/sdk)
                    // The dotnet root is its parent.
                    highestPath = sdkPath;
                }
            }
        }

        if(highestPath is null)
        {
            return null;
        }

        // The [path] in dotnet --list-sdks output is the directory containing
        // the SDK version folders (e.g., /usr/share/dotnet/sdk).
        // The dotnet root is the parent of that directory.
        var dotnetRoot = Path.GetDirectoryName(highestPath);
        return dotnetRoot;
    }

    /// <summary>
    /// Tries well-known .NET installation paths as a fallback.
    /// </summary>
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

        // Linux and other Unix
        return
        [
            "/usr/share/dotnet",
            "/usr/local/share/dotnet",
            Path.Combine(homePath, ".dotnet"),
        ];
    }

    /// <summary>
    /// Finds the highest version of <c>Microsoft.NETCore.App.Ref</c> that
    /// provides reference assemblies for the given target framework moniker.
    /// Path pattern: <c>{sdkRoot}/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/</c>
    /// </summary>
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

        // Extract the major version from the TFM (e.g., "net10.0" → 10)
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

            // Only consider versions whose major matches the TFM major
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

    /// <summary>
    /// Extracts the major version number from a TFM string.
    /// E.g., "net10.0" → 10, "net8.0" → 8, "net9.0" → 9.
    /// Returns null if the TFM cannot be parsed.
    /// </summary>
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

    private static string? ReadDiskCache()
    {
        try
        {
            if(!File.Exists(CacheFilePath))
            {
                return null;
            }

            var cached = File.ReadAllText(CacheFilePath).Trim();
            return string.IsNullOrEmpty(cached) ? null : cached;
        }
        catch
        {
            return null;
        }
    }

    private static void WriteDiskCache(string path)
    {
        try
        {
            var dir = Path.GetDirectoryName(CacheFilePath);
            if(dir is not null && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(CacheFilePath, path);
        }
        catch
        {
            // Best effort — cache write failure is non-fatal.
        }
    }
}