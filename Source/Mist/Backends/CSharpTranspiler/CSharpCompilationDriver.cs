using System.Collections.Immutable;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Mist.Backends.Contracts;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using RoslynApi = Microsoft.CodeAnalysis;

namespace Mist.Backends.CSharpTranspiler;

/// <summary>
/// Compiles in-process through Roslyn rather than spawning a <c>dotnet build</c> subprocess per
/// compilation.
/// </summary>
public sealed class CSharpCompilationDriver
{
    // Internal so in-process Roslyn verification tests reuse the same reference set.
    internal static readonly ImmutableArray<RoslynApi.MetadataReference> CoreReferences = BuildCoreReferences();

    private static readonly StringComparer PathComparer =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    private static ImmutableArray<RoslynApi.MetadataReference> BuildCoreReferences()
    {
        var runtimeDir = GetRuntimeDirectory();
        var refPackDir = TryResolveRefPackDir();

        var refs = new List<RoslynApi.MetadataReference>();

        // Every assembly in the pack, so transitive framework dependencies of packages (System.Data.Common
        // for EF Core) resolve without a manual list.
        if(refPackDir is not null)
        {
            foreach(var dll in Directory.GetFiles(refPackDir, "*.dll"))
            {
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(dll));
            }

            return [.. refs];
        }

        foreach(var name in new[]
        {
            "System.Runtime.dll",
            "System.Console.dll",
            "System.Collections.dll",
            "System.Linq.dll",
            "System.Text.RegularExpressions.dll",
            "System.Threading.dll",
            "netstandard.dll",
            "System.Private.CoreLib.dll",
            "System.Runtime.Extensions.dll",
        })
        {
            var path = Path.Combine(runtimeDir, name);
            if(File.Exists(path))
            {
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(path));
            }
        }

        // `Microsoft.CSharp` carries the DLR binder surface Roslyn compiles `dynamic` dispatch
        // against; `System.Linq.Expressions` and `System.Dynamic.Runtime` are transitively required
        // by the DLR's lowering.
        foreach(var name in new[]
        {
            "Microsoft.CSharp.dll",
            "System.Linq.Expressions.dll",
            "System.Dynamic.Runtime.dll",
        })
        {
            var path = Path.Combine(runtimeDir, name);
            if(File.Exists(path))
            {
                refs.Add(RoslynApi.MetadataReference.CreateFromFile(path));
            }
        }

        return [.. refs];
    }

    /// <summary>
    /// Null when only the runtime is installed, which leaves the caller on the shared-framework
    /// implementation assemblies.
    /// </summary>
    private static string? TryResolveRefPackDir()
    {
        var dotnetRoot = GetDotnetRoot();
        if(dotnetRoot is null)
        {
            return null;
        }

        var refPackRoot = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if(!Directory.Exists(refPackRoot))
        {
            return null;
        }

        var runtimeMajorMinor = $"{Environment.Version.Major}.{Environment.Version.Minor}.";
        var versionDirs = Directory.GetDirectories(refPackRoot)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .ToArray();

        var matched = versionDirs.FirstOrDefault(
            d => Path.GetFileName(d).StartsWith(runtimeMajorMinor, StringComparison.Ordinal));
        var selected = matched ?? versionDirs.FirstOrDefault();
        if(selected is null)
        {
            return null;
        }

        var refDir = Path.Combine(selected, "ref");
        if(!Directory.Exists(refDir))
        {
            return null;
        }

        var tfmDirs = Directory.GetDirectories(refDir)
            .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
            .ToArray();

        return tfmDirs.FirstOrDefault();
    }

    private static string GetRuntimeDirectory()
    {
        var assemblyLocation = typeof(object).Assembly.Location;
        if(!string.IsNullOrEmpty(assemblyLocation))
        {
            var dir = Path.GetDirectoryName(assemblyLocation);
            if(!string.IsNullOrEmpty(dir) && Directory.Exists(dir))
            {
                return dir;
            }
        }

        // Assembly.Location is empty under single-file publish.
        var dotnetRoot = GetDotnetRoot();
        if(dotnetRoot is not null)
        {
            var sharedDir = Path.Combine(dotnetRoot, "shared", "Microsoft.NETCore.App");
            if(Directory.Exists(sharedDir))
            {
                var versionDirs = Directory.GetDirectories(sharedDir)
                    .OrderByDescending(d => Path.GetFileName(d), StringComparer.Ordinal)
                    .ToArray();

                var runtimeVersion = Environment.Version.ToString();
                var exactMatch = versionDirs.FirstOrDefault(d =>
                    Path.GetFileName(d).StartsWith($"{Environment.Version.Major}.{Environment.Version.Minor}.", StringComparison.Ordinal));

                var selectedDir = exactMatch ?? versionDirs.FirstOrDefault();
                if(selectedDir is not null && Directory.Exists(selectedDir))
                {
                    return selectedDir;
                }
            }
        }

        // Last because it may be unavailable in trimmed apps.
        return System.Runtime.InteropServices.RuntimeEnvironment.GetRuntimeDirectory();
    }

    private static string? GetDotnetRoot()
    {
        var envRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if(!string.IsNullOrEmpty(envRoot) && Directory.Exists(envRoot))
        {
            return envRoot;
        }

        var candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
            }
            : new[]
            {
                "/usr/local/share/dotnet",
                "/usr/share/dotnet",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet"),
            };

        foreach(var candidate in candidates)
        {
            if(Directory.Exists(candidate) &&
                Directory.Exists(Path.Combine(candidate, "shared", "Microsoft.NETCore.App")))
            {
                return candidate;
            }
        }

        return null;
    }

    public static BackendResult Compile(
        string projectDirectory,
        string binDirectory,
        string projectName,
        DiagnosticBag diagnostics,
        int emittedFileCount = 0,
        string? mainTypeName = null,
        ImmutableArray<string> additionalReferencePaths = default,
        ProjectOutputType outputType = ProjectOutputType.Executable,
        ImmutableArray<ExternalAssembly> externalAssemblies = default,
        ImmutableArray<ComposedProject> composedProjects = default,
        IReadOnlyDictionary<string, DeclarationOrigin>? declarationOrigins = null,
        ImmutableArray<FrameworkReference> frameworkReferences = default,
        ImmutableArray<string> runtimeIdentifiers = default,
        ImmutableArray<RuntimeAsset> runtimeAssets = default)
    {
        if(!Directory.Exists(binDirectory))
        {
            _ = Directory.CreateDirectory(binDirectory);
        }

        var plan = ExternalReferencePlanner.Plan(
            projectName,
            additionalReferencePaths,
            externalAssemblies,
            composedProjects,
            frameworkReferences,
            AssemblyMetadataFiles.Instance,
            diagnostics);
        if(plan.Failed)
        {
            return new BackendResult(false, null, diagnostics.ToImmutableArray(), EmittedFileCount: emittedFileCount);
        }

        var referencePaths = plan.Paths;

        var csFiles = Directory.GetFiles(projectDirectory, "*.cs", SearchOption.AllDirectories);
        if(csFiles.Length == 0)
        {
            diagnostics.ReportTyped(BackendDiagnostics.BuildError, new Absent(AbsenceReason.Unavailable));
            return new BackendResult(false, null, diagnostics.ToImmutableArray(), EmittedFileCount: emittedFileCount);
        }

        var syntaxTrees = csFiles
            .Select(path => CSharpSyntaxTree.ParseText(
                File.ReadAllText(path),
                CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.Preview),
                path: path))
            .ToList();

        var roslynOutputKind = outputType == ProjectOutputType.Library
            ? RoslynApi.OutputKind.DynamicallyLinkedLibrary
            : RoslynApi.OutputKind.ConsoleApplication;
        var compilationOptions = new CSharpCompilationOptions(
            roslynOutputKind,
            optimizationLevel: RoslynApi.OptimizationLevel.Debug,
            nullableContextOptions: RoslynApi.NullableContextOptions.Enable,
            deterministic: true,
            mainTypeName: mainTypeName);

        var compilation = CSharpCompilation.Create(
            assemblyName: projectName,
            syntaxTrees: syntaxTrees,
            references: referencePaths.IsDefaultOrEmpty
                ? CoreReferences
                : CoreReferences.AddRange(
                    referencePaths
                        .Where(File.Exists)
                        .Select(p => (RoslynApi.MetadataReference)RoslynApi.MetadataReference.CreateFromFile(p))),
            options: compilationOptions);

        var facades = Facades(compilation, plan.Facades, declarationOrigins);
        if(!facades.IsEmpty)
        {
            compilation = compilation.AddReferences(
                facades.Select(facade => RoslynApi.MetadataReference.CreateFromImage(facade.Image)));
        }

        var outputPath = Path.Combine(binDirectory, $"{projectName}.dll");
        var runtimeConfigPath = Path.Combine(binDirectory, $"{projectName}.runtimeconfig.json");

        EmitResult result;
        using(var peStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
        {
            result = compilation.Emit(peStream);
        }

        var native = result.Diagnostics.Select(RoslynDiagnosticAdapter.Snapshot).ToImmutableArray();
        foreach(var occurrence in native)
        {
            RoslynDiagnosticAdapter.Admit(occurrence, diagnostics);
        }

        if(!result.Success)
        {
            if(File.Exists(outputPath))
            {
                File.Delete(outputPath);
            }

            return new BackendResult(false, null, diagnostics.ToImmutableArray(), EmittedFileCount: emittedFileCount, Native: native);
        }

        // Write a .runtimeconfig.json so `dotnet <dll>` knows which runtime to use
        WriteRuntimeConfig(runtimeConfigPath, referencePaths);

        var deployment = RuntimeAssetDeployment.Plan(
            binDirectory, referencePaths, runtimeAssets, externalAssemblies, runtimeIdentifiers, DeploymentFiles.Instance);
        Deploy(deployment.Copies);

        foreach(var facade in facades)
        {
            File.WriteAllBytes(Path.Combine(binDirectory, $"{facade.Name}.dll"), facade.Image.AsSpan());
        }

        return new BackendResult(
            true,
            outputPath,
            diagnostics.ToImmutableArray(),
            EmittedFileCount: emittedFileCount,
            RuntimeAssetsDeployed: deployment.RuntimeAssetsDeployed,
            Native: native);
    }

    private static void Deploy(ImmutableArray<FileCopy> copies)
    {
        foreach(var copy in copies)
        {
            try
            {
                File.Copy(copy.Source, copy.Destination, overwrite: true);
            }
            catch(Exception exception) when(exception is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    private static ImmutableArray<(string Name, ImmutableArray<byte> Image)> Facades(
        CSharpCompilation compilation,
        ImmutableArray<ComposedProject> facadeProjects,
        IReadOnlyDictionary<string, DeclarationOrigin>? declarationOrigins)
    {
        if(facadeProjects.IsDefaultOrEmpty)
        {
            return [];
        }

        var candidates = DeclaredTypes(compilation, declarationOrigins);
        var identity = compilation.Assembly.Identity;
        return [.. facadeProjects
            .GroupBy(project => project.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => (group.Key, FacadeEmitter.Emit(
                group.Key,
                identity.Name,
                identity.Version,
                group.SelectMany(project => FacadeTypeSelection.Select(candidates, project.ManifestPath)))))];
    }

    private static List<ForwardingCandidate> DeclaredTypes(
        CSharpCompilation compilation, IReadOnlyDictionary<string, DeclarationOrigin>? declarationOrigins)
    {
        var candidates = new List<ForwardingCandidate>();
        if(declarationOrigins is null)
        {
            return candidates;
        }

        var origins = new Dictionary<string, DeclarationOrigin>(PathComparer);
        foreach(var (path, origin) in declarationOrigins)
        {
            origins[Path.GetFullPath(path)] = origin;
        }

        var namespaces = new Stack<(RoslynApi.INamespaceSymbol Symbol, string Name)>();
        namespaces.Push((compilation.Assembly.GlobalNamespace, ""));
        while(namespaces.TryPop(out var current))
        {
            foreach(var member in current.Symbol.GetNamespaceMembers())
            {
                namespaces.Push((member, current.Name.Length == 0 ? member.Name : $"{current.Name}.{member.Name}"));
            }

            foreach(var type in current.Symbol.GetTypeMembers())
            {
                CollectDeclaredType(type, current.Name, type.MetadataName, origins, candidates);
            }
        }

        return candidates;
    }

    private static void CollectDeclaredType(
        RoslynApi.INamedTypeSymbol type,
        string namespaceName,
        string typeName,
        Dictionary<string, DeclarationOrigin> origins,
        List<ForwardingCandidate> candidates)
    {
        foreach(var declaration in type.DeclaringSyntaxReferences)
        {
            if(origins.TryGetValue(Path.GetFullPath(declaration.SyntaxTree.FilePath), out var origin))
            {
                candidates.Add(new ForwardingCandidate(
                    namespaceName, typeName, type.DeclaredAccessibility == RoslynApi.Accessibility.Public, origin));
            }
        }

        foreach(var nested in type.GetTypeMembers())
        {
            CollectDeclaredType(nested, namespaceName, $"{typeName}/{nested.MetadataName}", origins, candidates);
        }
    }

    private static void WriteRuntimeConfig(string path, ImmutableArray<string> additionalReferencePaths = default)
    {
        var tfm = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription;
        // Extract version like "10.0.3" from ".NET 10.0.3"
        var version = tfm.Replace(".NET ", "").Trim();
        var parts = version.Split('.');
        var majorMinor = parts.Length >= 2 ? $"{parts[0]}.{parts[1]}" : version;

        var includeAspNetCore = !additionalReferencePaths.IsDefaultOrEmpty &&
            additionalReferencePaths.Any(p =>
                p.Contains("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase));

        var json = includeAspNetCore
            ? $$"""
                {
                  "runtimeOptions": {
                    "tfm": "net{{majorMinor}}",
                    "frameworks": [
                      {
                        "name": "Microsoft.NETCore.App",
                        "version": "{{version}}"
                      },
                      {
                        "name": "Microsoft.AspNetCore.App",
                        "version": "{{version}}"
                      }
                    ]
                  }
                }
                """
            : $$"""
                {
                  "runtimeOptions": {
                    "tfm": "net{{majorMinor}}",
                    "framework": {
                      "name": "Microsoft.NETCore.App",
                      "version": "{{version}}"
                    }
                  }
                }
                """;
        File.WriteAllText(path, json);
    }
}