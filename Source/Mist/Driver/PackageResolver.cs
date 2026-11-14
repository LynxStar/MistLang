using System.Collections.Immutable;
using System.Text;
using System.Text.Json;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using NuGet.Versioning;

namespace Mist.Driver;

/// <summary>
/// Delegates to <c>dotnet restore</c> and reads <c>project.assets.json</c>, inheriting the SDK's
/// certificate trust, TFM fallback, runtime-package skipping and transitive resolution.
/// </summary>
public static class PackageResolver
{
    /// <summary>
    /// Read only for an assets file that names neither its package folders nor its restore packages path.
    /// </summary>
    private static readonly string DefaultPackagesFolder =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");

    public static PackageResolutionResult ResolvePackages(
        ImmutableArray<NuGetPackageReference> packages,
        string targetFramework,
        DiagnosticBag diagnostics)
    {
        return ResolvePackages(packages, default, targetFramework, diagnostics);
    }

    /// <param name="frameworkReferences">
    /// Shared frameworks whose assemblies are left out of the result, so MetadataLoadContext never loads one twice.
    /// </param>
    /// <param name="beforeRestore">
    /// Called once the diagnostics known without a feed are in <paramref name="diagnostics"/>, so a caller can
    /// show them before a restore that may run for seconds.
    /// </param>
    /// <param name="runtimeIdentifier">
    /// The target runtime identifier whose fallback order selects each package's runtime assets; <see langword="null"/>
    /// selects none.
    /// </param>
    public static PackageResolutionResult ResolvePackages(
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences,
        string targetFramework,
        DiagnosticBag diagnostics,
        Action? beforeRestore = null,
        string? runtimeIdentifier = null)
    {
        return ResolvePackages(
            packages, frameworkReferences, targetFramework, diagnostics, beforeRestore, DotnetRestoreProcess.Run, runtimeIdentifier);
    }

    internal static PackageResolutionResult ResolvePackages(
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences,
        string targetFramework,
        DiagnosticBag diagnostics,
        Action? beforeRestore,
        Func<string, string, (int ExitCode, string Stdout, string Stderr)> restore,
        string? runtimeIdentifier = null)
    {
        return ResolveValidated(packages, diagnostics, beforeRestore,
            valid => RestorePackages(valid, frameworkReferences, targetFramework, diagnostics, restore, runtimeIdentifier));
    }

    internal static PackageResolutionResult ResolveValidated(
        ImmutableArray<NuGetPackageReference> packages, DiagnosticBag diagnostics,
        Action? beforeRestore, Func<ImmutableArray<NuGetPackageReference>, PackageResolutionResult> restore)
    {
        var valid = ImmutableArray.CreateBuilder<NuGetPackageReference>();
        var failureCount = 0;
        foreach(var package in packages.IsDefault ? [] : packages)
        {
            if(NuGetVersion.TryParse(package.Version, out _))
            {
                valid.Add(package);
            }
            else
            {
                PackageDiagnosticFacts.InvalidVersion(package, diagnostics);
                failureCount++;
            }
        }

        if(valid.Count == 0)
        {
            return new PackageResolutionResult([], [], failureCount);
        }

        beforeRestore?.Invoke();
        var result = restore(valid.ToImmutable());
        return result with { FailureCount = result.FailureCount + failureCount };
    }

    private static PackageResolutionResult RestorePackages(
        ImmutableArray<NuGetPackageReference> packages,
        ImmutableArray<FrameworkReference> frameworkReferences,
        string targetFramework,
        DiagnosticBag diagnostics,
        Func<string, string, (int ExitCode, string Stdout, string Stderr)> restore,
        string? runtimeIdentifier)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"mist-restore-{Guid.NewGuid():N}");

        try
        {
            _ = Directory.CreateDirectory(tempDir);

            var csprojPath = GenerateCsproj(tempDir, packages, frameworkReferences, targetFramework);
            if(NuGetRestoreFailure.Invoke(() => restore(csprojPath, tempDir), csprojPath, diagnostics) is not { } outcome)
            {
                return new PackageResolutionResult([], [], packages.Length);
            }

            var (restoreExitCode, restoreStdout, restoreStderr) = outcome;

            if(restoreExitCode != 0)
            {
                return NuGetRestoreFailure.Report(packages, restoreStdout, restoreStderr, diagnostics);
            }

            var assetsPath = Path.Combine(tempDir, "obj", "project.assets.json");
            if(!File.Exists(assetsPath))
            {
                PackageDiagnosticFacts.Network(diagnostics, "", assetsMissing: assetsPath);
                return new PackageResolutionResult(
                    ImmutableArray<string>.Empty,
                    ImmutableArray<ResolvedPackageInfo>.Empty,
                    FailureCount: packages.Length);
            }

            return ParseAssetsFile(assetsPath, packages,
                targetFramework, 0, diagnostics, runtimeIdentifier);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            PackageDiagnosticFacts.Network(diagnostics, ex.Message, temporaryProject: tempDir);
            return new PackageResolutionResult(
                ImmutableArray<string>.Empty,
                ImmutableArray<ResolvedPackageInfo>.Empty,
                FailureCount: packages.Length);
        }
        finally
        {
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

    private static PackageResolutionResult ParseAssetsFile(
        string assetsPath,
        ImmutableArray<NuGetPackageReference> declaredPackages,
        string targetFramework,
        int priorFailureCount,
        DiagnosticBag diagnostics,
        string? runtimeIdentifier)
    {
        JsonDocument doc;
        try
        {
            var json = File.ReadAllText(assetsPath);
            doc = JsonDocument.Parse(json);
        }
        catch(Exception ex) when(ex is JsonException or IOException)
        {
            PackageDiagnosticFacts.Network(diagnostics, ex.Message, assetsRead: assetsPath);
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

            var frameworkAssemblyNames = BuildFrameworkAssemblySet(doc, targetFramework);

            var declaredPackageSet = new Dictionary<string, NuGetPackageReference>(
                StringComparer.OrdinalIgnoreCase);
            foreach(var pkg in declaredPackages)
            {
                declaredPackageSet[pkg.Name] = pkg;
            }

            var resolvedDeclaredPackages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var packageFolders = PackageAssets.PackageFolders(doc.RootElement, DefaultPackagesFolder);

            var targetEntry = PackageAssets.Target(doc.RootElement, targetFramework);

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

                    if(library.Value.TryGetProperty("type", out var typeElement)
                        && typeElement.GetString() == "project")
                    {
                        continue;
                    }

                    var packageDirectory = PackageAssets.PackageDirectory(doc.RootElement, packageFolders, library.Name);

                    string? selectedTfm = null;
                    var packageAssemblies = ImmutableArray.CreateBuilder<string>();

                    if(library.Value.TryGetProperty("compile", out var compileSection))
                    {
                        foreach(var assembly in compileSection.EnumerateObject())
                        {
                            var assemblyRelPath = assembly.Name;

                            // "_._" is a placeholder for an asset the runtime provides.
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
                                packageDirectory,
                                assemblyRelPath.Replace('/', Path.DirectorySeparatorChar));

                            if(File.Exists(fullPath))
                            {
                                packageAssemblies.Add(fullPath);
                            }
                        }
                    }

                    // Some packages list runtime assemblies and no compile ones.
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
                                packageDirectory,
                                assemblyRelPath.Replace('/', Path.DirectorySeparatorChar));

                            if(File.Exists(fullPath))
                            {
                                packageAssemblies.Add(fullPath);
                            }
                        }
                    }

                    assemblyPaths.AddRange(packageAssemblies);

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

            foreach(var pkg in declaredPackages)
            {
                if(!resolvedDeclaredPackages.Contains(pkg.Name))
                {
                    PackageDiagnosticFacts.Missing(pkg, "", null, diagnostics);
                    failureCount++;
                }
            }

            var result = new PackageResolutionResult(
                assemblyPaths.ToImmutable(),
                resolvedInfos.ToImmutable(),
                failureCount);
            if(runtimeIdentifier is null)
            {
                return result;
            }

            var graph = ReadRidGraph(PackageAssets.RuntimeIdentifierGraphPath(doc.RootElement, targetFramework));
            var order = RidGraph.FallbackOrder(graph ?? RidGraph.BuiltIn, runtimeIdentifier);

            return result with
            {
                RuntimeAssets = PackageAssets.SelectRuntimeAssets(doc.RootElement, targetFramework, packageFolders, order),
                RuntimeIdentifierGraph = graph,
            };
        }
    }

    private static RidGraph? ReadRidGraph(string? path)
    {
        if(path is null || !File.Exists(path))
        {
            return null;
        }

        try
        {
            return RidGraph.Parse(File.ReadAllText(path));
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static HashSet<string> BuildFrameworkAssemblySet(
        JsonDocument doc,
        string targetFramework)
    {
        var frameworkAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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

        // A package whose compile entries are all "_._" is runtime-provided.
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

                    if(hasAny && allPlaceholders)
                    {
                        var libParts = library.Name.Split('/');
                        if(libParts.Length == 2)
                        {
                            // The package name stands in for its assembly name, which matches for System.* packages.
                            _ = frameworkAssemblies.Add(libParts[0]);
                        }
                    }
                }
            }
        }

        return frameworkAssemblies;
    }
}