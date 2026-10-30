using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;

namespace Mist.Driver.Generators;

internal static class GeneratorPreparation
{
    public static ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>> Prepare(
        string folder, ProjectConfiguration configuration, ProjectReferenceResolution ordinary,
        DiagnosticBag diagnostics, ICompilationProgressReporter progress,
        ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>> overlay,
        GeneratorServices services)
    {
        var manifest = Path.GetFullPath(Path.Combine(folder, ProjectFileParser.FileName));
        var resolutions = new Dictionary<string, ProjectReferenceResolution>(StringComparer.Ordinal) { [manifest] = ordinary };
        var configurations = ordinary.Projects.ToDictionary(project => project.CanonicalTomlPath, project => project.Configuration, StringComparer.Ordinal);
        configurations[manifest] = configuration;
        var nodes = GeneratorGraph.Order(forGraph(new GeneratorNode(manifest, configuration)), path =>
        {
            if(configurations.TryGetValue(path, out var known))
            {
                return known;
            }

            if(!File.Exists(path))
            {
                var owner = configurations.First(pair => !pair.Value.Generators.IsDefaultOrEmpty
                    && pair.Value.Generators.Any(reference => reference.ProjectPath == path));
                Invalid(owner.Value.Generators.First(reference => reference.ProjectPath == path).Name,
                    owner.Key, "generator manifest is missing", diagnostics);
                return null;
            }

            var parsed = ProjectFileParser.Parse(Path.GetDirectoryName(path)!, diagnostics);
            if(parsed is not null)
            {
                configurations[path] = parsed;
            }

            return parsed;
        }, node =>
        {
            if(!resolutions.TryGetValue(node.Manifest, out var resolution))
            {
                resolution = ProjectReferenceResolver.ResolveAll(Path.GetDirectoryName(node.Manifest)!, node.Configuration.References, diagnostics);
                resolutions[node.Manifest] = resolution;
            }

            foreach(var project in resolution.Projects)
            {
                configurations[project.CanonicalTomlPath] = project.Configuration;
            }

            return resolution.Projects.Select(project => forGraph(new GeneratorNode(project.CanonicalTomlPath, project.Configuration)));
        }, diagnostics);
        foreach(var node in nodes)
        {
            GeneratorReferenceValidation.Validate(node, configurations, diagnostics);
        }

        if(diagnostics.HasErrors || nodes.All(node => node.Configuration.Generators.IsDefaultOrEmpty && node.Configuration.SeedSources is null))
        {
            return overlay;
        }

        GeneratorNode forGraph(GeneratorNode node)
        {
            return overlay.ContainsKey(node.Manifest)
                ? node with { Configuration = node.Configuration with { Generators = [] } } : node;
        }

        var watch = Stopwatch.StartNew();
        var events = new List<string>();
        progress.ReportStageStart("1.gen", "Source generation");
        try
        {
            foreach(var node in nodes)
            {
                if(overlay.ContainsKey(node.Manifest))
                {
                    continue;
                }

                var generated = ImmutableArray.CreateBuilder<ResolvedProjectReference>();
                if(node.Configuration.SeedSources is not null)
                {
                    if(GeneratorSeeds.Prepare(node, new GeneratorFiles(), diagnostics) is not { } seed)
                    {
                        return overlay;
                    }

                    generated.Add(Materialize(node, "SeedSources", seed, seedSources: true));
                }

                foreach(var reference in node.Configuration.Generators.IsDefault ? [] : node.Configuration.Generators)
                {
                    if(!configurations.TryGetValue(reference.ProjectPath, out var generator))
                    {
                        return overlay;
                    }

                    var toolchain = services.Toolchain(generator.TargetFramework, diagnostics);
                    if(toolchain is null)
                    {
                        return overlay;
                    }

                    var cache = new GeneratorCache(GeneratorCache.Root, toolchain, events.Add);
                    var host = new GeneratorHost(cache, new GeneratorFiles(), services.Run, services.Compile);
                    var inputHashes = ImmutableArray.CreateBuilder<ContentHash>();
                    foreach(var input in reference.Inputs)
                    {
                        if(!File.Exists(input))
                        {
                            diagnostics.ReportTyped(DriverDiagnostics.GeneratorInputNotFound, new ProjectLocation(node.Manifest, null), new PathValue(input));
                            return overlay;
                        }

                        inputHashes.Add(new ContentHash(input, GeneratorProvenance.Hash(File.ReadAllBytes(input))));
                    }

                    var generatorResolution = resolutions[reference.ProjectPath];
                    var externals = BuildExternalIdentities(reference, generator, generatorResolution, diagnostics, services);
                    if(diagnostics.HasErrors)
                    {
                        return overlay;
                    }

                    var compileKey = Fingerprint(reference.ProjectPath, toolchain, configurations, overlay, externals);
                    var request = new GeneratorRequest(toolchain, compileKey, node.Configuration.Name, node.Manifest, reference, generator, inputHashes.ToImmutable());
                    if(host.Prepare(request, diagnostics) is not { } entry)
                    {
                        return overlay;
                    }

                    if(reference.Seed)
                    {
                        diagnostics.ReportTyped(DriverDiagnostics.GeneratorSeedUsed, new ProjectLocation(node.Manifest, null), new PathValue(reference.ProjectPath));
                        if(diagnostics.HasErrors)
                        {
                            return overlay;
                        }
                    }

                    generated.Add(Materialize(node, reference.Name, entry));
                }

                overlay = overlay.SetItem(node.Manifest, generated.ToImmutable());
            }

            return overlay;
        }
        catch(Exception exception) when(exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            diagnostics.ReportTyped(DriverDiagnostics.GeneratorRunFailed, new ProjectLocation(manifest, null),
                new NameValue(configuration.Name), new TextValue("generator preparation could not access its files"));
            return overlay;
        }
        finally
        {
            progress.ReportStageComplete("1.gen", watch.Elapsed, events);
        }
    }

    internal static (string Name, string Folder) Location(GeneratorNode consumer, string generator, bool seedSources = false)
    {
        var root = Path.GetDirectoryName(consumer.Manifest)!;
        return seedSources
            ? ($"{consumer.Configuration.Name}.SeedSources", Path.Combine(root, "out", "seed-sources"))
            : ($"{consumer.Configuration.Name}.Generated.{generator}", Path.Combine(root, "out", "generated", generator));
    }

    internal static ResolvedProjectReference Materialize(GeneratorNode consumer, string generator, CachedGeneration entry, bool seedSources = false)
    {
        var (name, folder) = Location(consumer, generator, seedSources);
        if(Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        _ = Directory.CreateDirectory(folder);
        File.WriteAllText(Path.Combine(Path.GetDirectoryName(folder)!, SourcePathSelection.GeneratedMarker), "");
        var paths = ImmutableArray.CreateBuilder<string>();
        foreach(var output in entry.Provenance.Outputs)
        {
            var target = Path.Combine(folder, output.Path);
            _ = Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(Path.Combine(entry.Folder, output.Path), target);
            paths.Add(target);
        }

        return new ResolvedProjectReference(Path.Combine(folder, ProjectFileParser.FileName), folder,
            new ProjectConfiguration(name, name, [], GlobalUsings: consumer.Configuration.GlobalUsings,
                TargetFramework: consumer.Configuration.TargetFramework, OutputType: ProjectOutputType.Library), paths.ToImmutable());
    }

    private static string Fingerprint(string manifest, string toolchain, Dictionary<string, ProjectConfiguration> configurations,
        ImmutableDictionary<string, ImmutableArray<ResolvedProjectReference>> overlay,
        Dictionary<(string Manifest, string Name), string> externals)
    {
        var configuration = configurations[manifest];
        var folder = Path.GetDirectoryName(manifest)!;
        var sources = SourcePathSelection.ForReferencedProject(folder, SourcePathSelection.EnumerateCandidates(folder)).Prepend(manifest)
            .Order(StringComparer.Ordinal).Select(path => new ContentHash(path, GeneratorProvenance.Hash(File.ReadAllBytes(path)))).ToList();
        if(overlay.TryGetValue(manifest, out var generated))
        {
            sources.AddRange(generated.SelectMany(project => project.SourcePaths).Order(StringComparer.Ordinal)
                .Select(path => new ContentHash(path, GeneratorProvenance.Hash(File.ReadAllBytes(path)))));
        }

        var references = configuration.References.Select(reference => new ContentHash(reference.Path,
            configurations.ContainsKey(reference.Path)
                ? Fingerprint(reference.Path, toolchain, configurations, overlay, externals)
                : externals.GetValueOrDefault((manifest, reference.Name), "missing"))).ToArray();
        return GeneratorKeys.Compile(toolchain, sources, references, RuntimeInformation.RuntimeIdentifier);
    }

    private static Dictionary<(string Manifest, string Name), string> BuildExternalIdentities(
        GeneratorReference reference, ProjectConfiguration generator, ProjectReferenceResolution resolution,
        DiagnosticBag diagnostics, GeneratorServices services)
    {
        var result = new Dictionary<(string Manifest, string Name), string>();
        if(resolution.ExternalReferences.IsEmpty)
        {
            return result;
        }

        var chain = BuildChain.Format(BuildChain.Extend(BuildChain.Current(), reference.ProjectPath));
        var (assemblies, _) = CSharpProjectBuilder.BuildAll(resolution.ExternalReferences, generator.TargetFramework, chain, diagnostics, services.Run);
        if(diagnostics.HasErrors)
        {
            diagnostics.ReportTyped(DriverDiagnostics.GeneratorBuildFailed, new ProjectLocation(reference.ProjectPath, null), new NameValue(reference.Name));
            return result;
        }

        foreach(var external in resolution.ExternalReferences)
        {
            var assembly = assemblies.First(item => item.Name == external.Name);
            var files = assembly.Closure.Prepend(assembly.Path);
            if(assembly.RuntimeConfigPath is not null)
            {
                files = files.Append(assembly.RuntimeConfigPath);
            }

            result[(external.DeclaringManifestPath, external.Name)] = GeneratorKeys.Compile("external",
                files.Order(StringComparer.Ordinal).Select(path => new ContentHash(path, GeneratorProvenance.Hash(File.ReadAllBytes(path)))), [], "");
        }

        return result;
    }

    private static void Invalid(string name, string manifest, string reason, DiagnosticBag diagnostics)
    {
        diagnostics.ReportTyped(DriverDiagnostics.GeneratorReferenceInvalid, new ProjectLocation(manifest, null), new NameValue(name), new TextValue(reason));
    }
}