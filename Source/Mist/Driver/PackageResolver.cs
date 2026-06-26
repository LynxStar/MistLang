using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using NuGet.Versioning;

namespace Mist.Driver;

/// <summary>
/// Resolves NuGet package references to assembly file paths by delegating to
/// <c>dotnet restore</c> and parsing the resulting <c>project.assets.json</c>.
/// This approach inherits the SDK's certificate trust, TFM fallback, runtime-
/// package skipping, and transitive dependency resolution automatically.
/// </summary>
public static class PackageResolver
{
    /// <summary>
    /// The standard NuGet global packages folder (~/.nuget/packages/).
    /// </summary>
    private static readonly string GlobalPackagesFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    /// <summary>
    /// Maximum time allowed for the <c>dotnet restore</c> process to complete.
    /// </summary>
    private static readonly TimeSpan RestoreTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Resolves all package references to assembly paths by generating a temporary
    /// .csproj, running <c>dotnet restore</c>, and parsing <c>project.assets.json</c>.
    /// </summary>
    /// <param name="packages">The declared package references from mist.toml.</param>
    /// <param name="targetFramework">The project's target framework (e.g., "net10.0").</param>
    /// <param name="diagnostics">Diagnostic bag for reporting errors.</param>
    /// <returns>
    /// A <see cref="PackageResolutionResult"/> carrying the flattened assembly
    /// paths, a per-declared-package summary, and the count of declared packages
    /// that failed to resolve.
    /// </returns>
    public static PackageResolutionResult ResolvePackages(
        ImmutableArray<NuGetPackageReference> packages,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        return ResolvePackages(packages, default, targetFramework, diagnostics);
    }

    /// <summary>
    /// Resolves all package references to assembly paths, deduplicating against
    /// assemblies already provided by the specified shared framework references.
    /// </summary>
    /// <param name="packages">The declared package references from mist.toml.</param>
    /// <param name="frameworkReferences">
    /// Shared framework references (e.g., Microsoft.AspNetCore.App) whose assemblies
    /// should be excluded from the package resolution output to prevent duplicate
    /// assembly loading in MetadataLoadContext.
    /// </param>
    /// <param name="targetFramework">The project's target framework (e.g., "net10.0").</param>
    /// <param name="diagnostics">Diagnostic bag for reporting errors.</param>
    /// <returns>
    /// A <see cref="PackageResolutionResult"/> carrying the flattened assembly
    /// paths (excluding framework-provided assemblies), a per-declared-package
    /// summary, and the count of declared packages that failed to resolve.
    /// </returns>
    public static PackageResolutionResult ResolvePackages(
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        if(packages.IsDefaultOrEmpty)
        {
            return new PackageResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedPackageInfo>.Empty,
                FailureCount: 0);
        }

        // Validate version strings up-front (same behavior as before — invalid
        // versions produce MIST_PKG_002 without hitting the network).
        var validPackages = ImmutableArray.CreateBuilder<NuGetPackageReference>();
        var failureCount = 0;

        foreach(var pkg in packages)
        {
            if(!NuGetVersion.TryParse(pkg.Version, out _))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNotFound,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Package '{pkg.Name}' version '{pkg.Version}' could not be found on any configured NuGet feed (https://api.nuget.org/v3/index.json); the version string is not a valid NuGet version.",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
                failureCount++;
                continue;
            }

            validPackages.Add(pkg);
        }

        if(validPackages.Count == 0)
        {
            return new PackageResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedPackageInfo>.Empty,
                FailureCount: failureCount);
        }

        // Generate a temporary .csproj and run dotnet restore
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist-restore-{Guid.NewGuid():N}");

        try
        {
            _ = Directory.CreateDirectory(tempDir);

            var csprojPath = GenerateCsproj(tempDir, validPackages.ToImmutable(), frameworkReferences, targetFramework);
            var (restoreExitCode, restoreStdout, restoreStderr) = RunDotnetRestore(csprojPath, tempDir);

            if(restoreExitCode != 0)
            {
                // Parse the restore output to identify which packages failed
                return HandleRestoreFailure(
                    validPackages.ToImmutable(), restoreStdout, restoreStderr,
                    failureCount, diagnostics);
            }

            // Parse project.assets.json to extract resolved assembly paths
            var assetsPath = Path.Combine(tempDir, "obj", "project.assets.json");
            if(!File.Exists(assetsPath))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNetworkError,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    "Package restore completed but project.assets.json was not generated.",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
                return new PackageResolutionResult(
                    ImmutableArray<string>.Empty,
                    ImmutableArray<ResolvedPackageInfo>.Empty,
                    FailureCount: failureCount + validPackages.Count);
            }

            return ParseAssetsFile(assetsPath, validPackages.ToImmutable(), frameworkReferences,
                targetFramework, failureCount, diagnostics);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.PackageNetworkError,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Failed to create temporary restore project: {ex.Message}",
                Location: default,
                CompilationUnitPath: string.Empty,
                AnalyzerName: null));
            return new PackageResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedPackageInfo>.Empty,
                FailureCount: failureCount + validPackages.Count);
        }
        finally
        {
            // Clean up temp directory
            try
            {
                if(Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, recursive: true);
                }
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    /// <summary>
    /// Generates a minimal .csproj file with the declared package references.
    /// </summary>
    private static string GenerateCsproj(
        string directory,
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences,
        string targetFramework)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine("<Project Sdk=\"Microsoft.NET.Sdk\">");
        _ = sb.AppendLine("  <PropertyGroup>");
        _ = sb.AppendLine($"    <TargetFramework>{targetFramework}</TargetFramework>");
        // Suppress all warnings and informational output from the restore
        _ = sb.AppendLine("    <NoWarn>$(NoWarn);NU1701;NU1603</NoWarn>");
        _ = sb.AppendLine("  </PropertyGroup>");

        _ = sb.AppendLine("  <ItemGroup>");
        foreach(var pkg in packages)
        {
            _ = sb.AppendLine($"    <PackageReference Include=\"{pkg.Name}\" Version=\"{pkg.Version}\" />");
        }

        _ = sb.AppendLine("  </ItemGroup>");

        if(!frameworkReferences.IsDefaultOrEmpty)
        {
            _ = sb.AppendLine("  <ItemGroup>");
            foreach(var fw in frameworkReferences)
            {
                _ = sb.AppendLine($"    <FrameworkReference Include=\"{fw.Name}\" />");
            }

            _ = sb.AppendLine("  </ItemGroup>");
        }

        _ = sb.AppendLine("</Project>");

        var csprojPath = Path.Combine(directory, "_MistRestore.csproj");
        File.WriteAllText(csprojPath, sb.ToString());
        return csprojPath;
    }

    /// <summary>
    /// Runs <c>dotnet restore</c> on the generated .csproj.
    /// </summary>
    private static (int ExitCode, string Stdout, string Stderr) RunDotnetRestore(
        string csprojPath, string workingDirectory)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"restore \"{csprojPath}\" --verbosity quiet",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = new Process { StartInfo = psi };

        var stdoutBuilder = new StringBuilder();
        var stderrBuilder = new StringBuilder();

        process.OutputDataReceived += (_, e) =>
        {
            if(e.Data is not null)
            {
                _ = stdoutBuilder.AppendLine(e.Data);
            }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if(e.Data is not null)
            {
                _ = stderrBuilder.AppendLine(e.Data);
            }
        };

        _ = process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var completed = process.WaitForExit((int)RestoreTimeout.TotalMilliseconds);

        if(!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best effort */ }

            return (-1, stdoutBuilder.ToString(), "Restore timed out after 120 seconds.");
        }

        return (process.ExitCode, stdoutBuilder.ToString(), stderrBuilder.ToString());
    }

    /// <summary>
    /// Handles a failed <c>dotnet restore</c> by parsing the error output to
    /// determine which packages failed and emitting appropriate diagnostics.
    /// </summary>
    private static PackageResolutionResult HandleRestoreFailure(
        ImmutableArray<NuGetPackageReference> validPackages,
        string stdout,
        string stderr,
        int priorFailureCount,
        DiagnosticBag diagnostics)
    {
        var combinedOutput = $"{stdout}\n{stderr}";
        var packageFailures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try to identify specific package failures from the output
        foreach(var pkg in validPackages)
        {
            if(combinedOutput.Contains(pkg.Name, StringComparison.OrdinalIgnoreCase))
            {
                _ = packageFailures.Add(pkg.Name);
            }
        }

        // If we can't identify specific failures, report all as failed
        if(packageFailures.Count == 0)
        {
            // Check if it's a network/connection issue
            if(combinedOutput.Contains("Unable to load the service index", StringComparison.OrdinalIgnoreCase)
                || combinedOutput.Contains("network", StringComparison.OrdinalIgnoreCase)
                || combinedOutput.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                || combinedOutput.Contains("connection", StringComparison.OrdinalIgnoreCase)
                || combinedOutput.Contains("certificate", StringComparison.OrdinalIgnoreCase)
                || combinedOutput.Contains("signature", StringComparison.OrdinalIgnoreCase))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNetworkError,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Connection or network failure while restoring packages: {stderr.Trim()}",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
            }
            else
            {
                // Generic failure — report each package individually
                foreach(var pkg in validPackages)
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.PackageNotFound,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Package '{pkg.Name}' version '{pkg.Version}' was not found on any configured NuGet feed. Searched: https://api.nuget.org/v3/index.json.",
                        Location: default,
                        CompilationUnitPath: string.Empty,
                        AnalyzerName: null));
                }
            }

            return new PackageResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedPackageInfo>.Empty,
                FailureCount: priorFailureCount + validPackages.Length);
        }

        // Report individual package failures
        var resolvedInfos = ImmutableArray.CreateBuilder<ResolvedPackageInfo>();
        var newFailures = 0;

        foreach(var pkg in validPackages)
        {
            if(packageFailures.Contains(pkg.Name))
            {
                diagnostics.Add(new Diagnostic(
                    DiagnosticId.PackageNotFound,
                    DiagnosticSeverity.Error,
                    PipelineStage.Driver,
                    $"Package '{pkg.Name}' version '{pkg.Version}' was not found on any configured NuGet feed. Searched: https://api.nuget.org/v3/index.json.",
                    Location: default,
                    CompilationUnitPath: string.Empty,
                    AnalyzerName: null));
                newFailures++;
            }
        }

        return new PackageResolutionResult(
            ImmutableArray<string>.Empty,
            resolvedInfos.ToImmutable(),
            FailureCount: priorFailureCount + newFailures);
    }

    /// <summary>
    /// Parses <c>project.assets.json</c> to extract resolved assembly paths for
    /// the target framework. Deduplicates against assemblies provided by referenced
    /// shared frameworks to prevent duplicate assembly loading.
    /// </summary>
    private static PackageResolutionResult ParseAssetsFile(
        string assetsPath,
        ImmutableArray<NuGetPackageReference> declaredPackages,
        ImmutableArray<FrameworkReference> frameworkReferences,
        string targetFramework,
        int priorFailureCount,
        DiagnosticBag diagnostics)
    {
        JsonDocument doc;
        try
        {
            var json = File.ReadAllText(assetsPath);
            doc = JsonDocument.Parse(json);
        }
        catch(Exception ex) when(ex is JsonException or IOException)
        {
            diagnostics.Add(new Diagnostic(
                DiagnosticId.PackageNetworkError,
                DiagnosticSeverity.Error,
                PipelineStage.Driver,
                $"Failed to parse project.assets.json: {ex.Message}",
                Location: default,
                CompilationUnitPath: string.Empty,
                AnalyzerName: null));
            return new PackageResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedPackageInfo>.Empty,
                FailureCount: priorFailureCount + declaredPackages.Length);
        }

        using(doc)
        {
            var assemblyPaths = ImmutableArray.CreateBuilder<string>();
            var resolvedInfos = ImmutableArray.CreateBuilder<ResolvedPackageInfo>();
            var failureCount = priorFailureCount;

            // Build set of framework-provided assembly names for deduplication (issue 3).
            var frameworkAssemblyNames = BuildFrameworkAssemblySet(doc, targetFramework);

            // Track which declared packages we successfully resolved
            var declaredPackageSet = new Dictionary<string, NuGetPackageReference>(
                StringComparer.OrdinalIgnoreCase);
            foreach(var pkg in declaredPackages)
            {
                declaredPackageSet[pkg.Name] = pkg;
            }

            var resolvedDeclaredPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Parse the "targets" section to get resolved libraries and their assemblies.
            // The target key is the TFM (e.g., "net10.0" or ".NETCoreApp,Version=v10.0").
            if(doc.RootElement.TryGetProperty("targets", out var targets))
            {
                // Find the target entry that matches our framework
                JsonElement? targetEntry = null;
                foreach(var target in targets.EnumerateObject())
                {
                    // The key can be either short form (net10.0) or long form
                    // (.NETCoreApp,Version=v10.0). Check both.
                    if(target.Name.Contains(targetFramework, StringComparison.OrdinalIgnoreCase)
                        || target.Name.Contains(GetLongFrameworkName(targetFramework), StringComparison.OrdinalIgnoreCase))
                    {
                        targetEntry = target.Value;
                        break;
                    }
                }

                // If we didn't find an exact match, take the first (there's usually only one)
                if(targetEntry is null)
                {
                    foreach(var target in targets.EnumerateObject())
                    {
                        targetEntry = target.Value;
                        break;
                    }
                }

                if(targetEntry.HasValue)
                {
                    foreach(var library in targetEntry.Value.EnumerateObject())
                    {
                        // Library key format: "PackageName/Version"
                        var parts = library.Name.Split('/');
                        if(parts.Length != 2)
                        {
                            continue;
                        }

                        var packageName = parts[0];
                        var packageVersion = parts[1];

                        // Skip if this is a "project" type (not a package)
                        if(library.Value.TryGetProperty("type", out var typeElement)
                            && typeElement.GetString() == "project")
                        {
                            continue;
                        }

                        // Extract compile-time assemblies from "compile" property
                        string? selectedTfm = null;
                        var packageAssemblies = ImmutableArray.CreateBuilder<string>();

                        if(library.Value.TryGetProperty("compile", out var compileSection))
                        {
                            foreach(var assembly in compileSection.EnumerateObject())
                            {
                                var assemblyRelPath = assembly.Name;

                                // Skip placeholder "_._" entries (runtime-provided packages)
                                if(assemblyRelPath.EndsWith("_._", StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                // Extract TFM from path (e.g., "lib/net8.0/Assembly.dll")
                                var pathParts = assemblyRelPath.Split('/');
                                if(pathParts.Length >= 3 && pathParts[0] == "lib")
                                {
                                    selectedTfm = pathParts[1];
                                }

                                var assemblyName = Path.GetFileNameWithoutExtension(
                                    Path.GetFileName(assemblyRelPath));

                                // Deduplicate against framework-provided assemblies (issue 3)
                                if(frameworkAssemblyNames.Contains(assemblyName))
                                {
                                    continue;
                                }

                                // Resolve to full path in global packages folder
                                var fullPath = Path.Combine(
                                    GlobalPackagesFolder,
                                    packageName.ToLowerInvariant(),
                                    packageVersion.ToLowerInvariant(),
                                    assemblyRelPath.Replace('/', Path.DirectorySeparatorChar));

                                if(File.Exists(fullPath))
                                {
                                    packageAssemblies.Add(fullPath);
                                }
                            }
                        }

                        // Also check "runtime" assemblies as fallback (some packages
                        // only specify runtime, not compile)
                        if(packageAssemblies.Count == 0
                            && library.Value.TryGetProperty("runtime", out var runtimeSection))
                        {
                            foreach(var assembly in runtimeSection.EnumerateObject())
                            {
                                var assemblyRelPath = assembly.Name;

                                if(assemblyRelPath.EndsWith("_._", StringComparison.Ordinal))
                                {
                                    continue;
                                }

                                var pathParts = assemblyRelPath.Split('/');
                                if(pathParts.Length >= 3 && pathParts[0] == "lib")
                                {
                                    selectedTfm = pathParts[1];
                                }

                                var assemblyName = Path.GetFileNameWithoutExtension(
                                    Path.GetFileName(assemblyRelPath));

                                if(frameworkAssemblyNames.Contains(assemblyName))
                                {
                                    continue;
                                }

                                var fullPath = Path.Combine(
                                    GlobalPackagesFolder,
                                    packageName.ToLowerInvariant(),
                                    packageVersion.ToLowerInvariant(),
                                    assemblyRelPath.Replace('/', Path.DirectorySeparatorChar));

                                if(File.Exists(fullPath))
                                {
                                    packageAssemblies.Add(fullPath);
                                }
                            }
                        }

                        assemblyPaths.AddRange(packageAssemblies);

                        // Record info for declared (direct) packages
                        if(declaredPackageSet.ContainsKey(packageName))
                        {
                            _ = resolvedDeclaredPackages.Add(packageName);
                            resolvedInfos.Add(new ResolvedPackageInfo(
                                packageName,
                                packageVersion,
                                selectedTfm ?? string.Empty,
                                packageAssemblies.Count));
                        }
                    }
                }
            }

            // Check for declared packages that didn't appear in the assets file
            foreach(var pkg in declaredPackages)
            {
                if(!resolvedDeclaredPackages.Contains(pkg.Name))
                {
                    diagnostics.Add(new Diagnostic(
                        DiagnosticId.PackageNotFound,
                        DiagnosticSeverity.Error,
                        PipelineStage.Driver,
                        $"Package '{pkg.Name}' version '{pkg.Version}' was not found on any configured NuGet feed. Searched: https://api.nuget.org/v3/index.json.",
                        Location: default,
                        CompilationUnitPath: string.Empty,
                        AnalyzerName: null));
                    failureCount++;
                }
            }

            return new PackageResolutionResult(
                assemblyPaths.ToImmutable(),
                resolvedInfos.ToImmutable(),
                failureCount);
        }
    }

    /// <summary>
    /// Builds a set of assembly names that are provided by the referenced shared
    /// frameworks. Uses the "frameworkAssemblies" section of project.assets.json
    /// when available, falling back to the "frameworkReferences" in the target's
    /// libraries.
    /// </summary>
    private static HashSet<string> BuildFrameworkAssemblySet(
        JsonDocument doc,
        string targetFramework)
    {
        var frameworkAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Try "project" → "frameworks" → tfm → "frameworkAssemblies" in assets file
        if(doc.RootElement.TryGetProperty("project", out var project)
            && project.TryGetProperty("frameworks", out var frameworks))
        {
            foreach(var fw in frameworks.EnumerateObject())
            {
                if(fw.Value.TryGetProperty("frameworkAssemblies", out var fwAssemblies))
                {
                    foreach(var asm in fwAssemblies.EnumerateObject())
                    {
                        _ = frameworkAssemblies.Add(asm.Name);
                    }
                }
            }
        }

        // Also scan the targets for libraries with type "package" that have only
        // "_._" compile entries — these are runtime-provided and should be excluded
        if(doc.RootElement.TryGetProperty("targets", out var targets))
        {
            foreach(var target in targets.EnumerateObject())
            {
                if(!target.Name.Contains(targetFramework, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach(var library in target.Value.EnumerateObject())
                {
                    if(!library.Value.TryGetProperty("compile", out var compile))
                    {
                        continue;
                    }

                    var allPlaceholders = true;
                    var hasAny = false;

                    foreach(var entry in compile.EnumerateObject())
                    {
                        hasAny = true;
                        if(!entry.Name.EndsWith("_._", StringComparison.Ordinal))
                        {
                            allPlaceholders = false;
                            break;
                        }
                    }

                    // If all compile entries are "_._" placeholders, this package
                    // is runtime-provided. Add its name to the exclusion set.
                    if(hasAny && allPlaceholders)
                    {
                        var libParts = library.Name.Split('/');
                        if(libParts.Length == 2)
                        {
                            // The package name itself (not assembly name) is what
                            // we track here — but typically the assembly name matches
                            // the package name for System.* packages.
                            _ = frameworkAssemblies.Add(libParts[0]);
                        }
                    }
                }
            }
        }

        return frameworkAssemblies;
    }

    /// <summary>
    /// Converts a short TFM name (e.g., "net10.0") to the long-form NuGet target
    /// name (e.g., ".NETCoreApp,Version=v10.0") for matching in assets.json.
    /// </summary>
    private static string GetLongFrameworkName(string shortTfm)
    {
        // net10.0 → .NETCoreApp,Version=v10.0
        if(shortTfm.StartsWith("net", StringComparison.Ordinal) && shortTfm.Contains('.'))
        {
            var versionPart = shortTfm[3..]; // "10.0"
            return $".NETCoreApp,Version=v{versionPart}";
        }

        return shortTfm;
    }
}