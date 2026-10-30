using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.Driver.Contracts;
using Tomlyn.Model;

namespace Mist.Driver;

public static partial class ProjectFileParser
{
    internal static ProjectConfiguration? ReadGenerators(
        TomlTable model, string manifest, ProjectConfiguration configuration, DiagnosticBag diagnostics)
    {
        var projectType = MistProjectType.Regular;
        if(model.TryGetValue("project_type", out var type))
        {
            if(type is not string spelling || spelling is not ("regular" or "source_generator"))
            {
                diagnostics.ReportTyped(DriverDiagnostics.GeneratorProjectInvalid, new ProjectLocation(manifest, null),
                    new TextValue("project_type must be regular or source_generator"));
                return null;
            }

            projectType = spelling == "source_generator" ? MistProjectType.SourceGenerator : MistProjectType.Regular;
        }

        if(projectType == MistProjectType.SourceGenerator && configuration.OutputType != ProjectOutputType.Executable)
        {
            diagnostics.ReportTyped(DriverDiagnostics.GeneratorProjectInvalid, new ProjectLocation(manifest, null),
                new TextValue("a source generator must be executable"));
            return null;
        }

        var folder = Path.GetDirectoryName(manifest)!;
        var generators = ImmutableArray.CreateBuilder<GeneratorReference>();
        if(model.TryGetValue("source_generators", out var value))
        {
            if(value is not TomlTable table)
            {
                return invalid("source_generators", "expected a table");
            }

            foreach(var (name, entry) in table)
            {
                if(string.IsNullOrWhiteSpace(name) || name is "." or ".." || !ValidNamePattern().IsMatch(name)
                    || $"{configuration.Name}.Generated.{name}".Length > MaxNameLength)
                {
                    return invalid(name, "invalid generator name or generated identity exceeds 128 characters");
                }

                if(entry is not TomlTable fields || !fields.TryGetValue("path", out var pathValue)
                    || pathValue is not string path || !ValidGeneratorPath(path))
                {
                    return invalid(name, "path must be a nonempty string");
                }

                if(!fields.TryGetValue("inputs", out var inputValue) || inputValue is not TomlArray { Count: > 0 } inputs
                    || inputs.Any(input => input is not string text || !ValidGeneratorPath(text)))
                {
                    return invalid(name, "inputs must be a nonempty array of nonempty paths");
                }

                var arguments = ImmutableArray<string>.Empty;
                if(fields.TryGetValue("arguments", out var argumentValue))
                {
                    if(argumentValue is not TomlArray array || array.Any(argument => argument is not string))
                    {
                        return invalid(name, "arguments must be an array of strings");
                    }

                    arguments = [.. array.Cast<string>()];
                }

                var seconds = 600L;
                if(fields.TryGetValue("timeout_seconds", out var timeout))
                {
                    if(timeout is not long limit || limit <= 0 || limit > int.MaxValue / 1000)
                    {
                        return invalid(name, "timeout_seconds must be a positive integer within the process timeout range");
                    }

                    seconds = limit;
                }

                var seed = false;
                if(fields.TryGetValue("seed", out var seedValue))
                {
                    if(seedValue is not bool flag)
                    {
                        return invalid(name, "seed must be a boolean");
                    }

                    seed = flag;
                }

                try
                {
                    var projectPath = Path.GetFullPath(path, folder);
                    if(Path.GetFileName(projectPath) != FileName)
                    {
                        projectPath = Path.Combine(projectPath, FileName);
                    }

                    generators.Add(new GeneratorReference(name, projectPath,
                        [.. inputs.Cast<string>().Select(input => Path.GetFullPath(input, folder))], arguments, TimeSpan.FromSeconds(seconds), seed));
                }
                catch(ArgumentException)
                {
                    return invalid(name, "invalid path");
                }
            }
        }

        SeedSources? seedSources = null;
        if(model.TryGetValue("seed_sources", out var sourceValue))
        {
            if(sourceValue is not TomlTable sources || !sources.TryGetValue("path", out var pathValue)
                || pathValue is not string path || !ValidGeneratorPath(path)
                || !sources.TryGetValue("provenance", out var recordValue)
                || recordValue is not string record || !ValidGeneratorPath(record)
                || $"{configuration.Name}.Generated.SeedSources".Length > MaxNameLength)
            {
                return invalid("seed_sources", "expected path and provenance strings and a generated identity within 128 characters");
            }

            try
            {
                seedSources = new SeedSources(Path.GetFullPath(path, folder), Path.GetFullPath(record, folder));
            }
            catch(ArgumentException)
            {
                return invalid("seed_sources", "invalid path");
            }
        }

        return configuration with { ProjectType = projectType, Generators = generators.ToImmutable(), SeedSources = seedSources };

        ProjectConfiguration? invalid(string name, string reason)
        {
            diagnostics.ReportTyped(DriverDiagnostics.GeneratorReferenceInvalid, new ProjectLocation(manifest, null),
                new NameValue(name), new TextValue(reason));
            return null;
        }
    }

    private static bool ValidGeneratorPath(string path)
    {
        return !string.IsNullOrWhiteSpace(path) && path.AsSpan().IndexOfAny('\0', '\r', '\n') < 0;
    }

}