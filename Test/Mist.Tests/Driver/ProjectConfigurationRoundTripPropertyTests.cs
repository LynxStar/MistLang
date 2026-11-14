using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

public sealed class ProjectConfigurationRoundTripPropertyTests
{
    private static Gen<string> GenProjectName()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        return Gen.Choose(1, 20).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(validChars), len)
                .Select(chars => new string(chars)));
    }

    private static Gen<string> GenIdentifier()
    {
        var startChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(0, 8).SelectMany(restLen =>
                Gen.ArrayOf(Gen.Elements(restChars), restLen)
                    .Select(rest => first + new string(rest))));
    }

    private static Gen<string> GenNamespace()
    {
        return Gen.Choose(1, 4).SelectMany(segmentCount =>
            Gen.ArrayOf(GenIdentifier(), segmentCount)
                .Select(segments => string.Join(".", segments)));
    }

    private static Gen<ImmutableArray<string>> GenGlobalUsings()
    {
        return Gen.Choose(0, 10).SelectMany(count =>
            Gen.ArrayOf(GenNamespace(), count + 5)
                .Select(namespaces =>
                {
                    var distinct = namespaces.Distinct(StringComparer.Ordinal).Take(count).ToArray();
                    return distinct.ToImmutableArray();
                }));
    }

    private static Gen<ProjectConfiguration> GenProjectConfiguration()
    {
        return GenProjectName().SelectMany(name =>
            GenProjectName().SelectMany(nsRoot =>
                Gen.Choose(0, 10).SelectMany(refCount =>
                    Gen.ArrayOf(GenProjectName(), refCount + 10).SelectMany(refNames =>
                        GenGlobalUsings().Select(globalUsings =>
                        {
                            var distinctNames = refNames
                                .Distinct(StringComparer.Ordinal)
                                .Take(refCount)
                                .ToArray();

                            var refs = distinctNames
                                .Select((r, i) => (ReferenceKind)(i % 3) switch
                                {
                                    ReferenceKind.CSharpProject => new ProjectReference(r, $"/refs/{r}/{r}.csproj", ReferenceKind.CSharpProject),
                                    ReferenceKind.Assembly => new ProjectReference(r, $"/refs/{r}/{r}.dll", ReferenceKind.Assembly),
                                    _ => new ProjectReference(r, $"/refs/{r}/mist.toml"),
                                })
                                .ToImmutableArray();

                            return new ProjectConfiguration(name, nsRoot, refs, GlobalUsings: globalUsings);
                        })))));
    }

    private static string SerializeToToml(ProjectConfiguration config)
    {
        var sb = new StringBuilder();
        _ = sb.AppendLine($"name = \"{EscapeTomlString(config.Name)}\"");
        _ = sb.AppendLine($"namespace_root = \"{EscapeTomlString(config.NamespaceRoot)}\"");

        if(!config.GlobalUsings.IsDefaultOrEmpty)
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine("[global_usings]");
            var nsEntries = string.Join(", ", config.GlobalUsings.Select(ns => $"\"{EscapeTomlString(ns)}\""));
            _ = sb.AppendLine($"namespaces = [{nsEntries}]");
        }

        if(!config.References.IsDefaultOrEmpty)
        {
            _ = sb.AppendLine();
            _ = sb.AppendLine("[references]");
            foreach(var reference in config.References)
            {
                // Quote the reference name to handle dots (which TOML interprets as nested keys)
                _ = sb.AppendLine($"\"{EscapeTomlString(reference.Name)}\" = {{ path = \"{EscapeTomlString(reference.Path)}\" }}");
            }
        }

        return sb.ToString();
    }

    private static string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"");
    }

    public static Arbitrary<ProjectConfiguration> ArbProjectConfiguration()
    {
        return GenProjectConfiguration().ToArbitrary();
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ProjectConfigurationRoundTripPropertyTests) })]
    public bool RoundTrip_SerializeAndParse_ProducesEqualConfiguration(ProjectConfiguration original)
    {
        var toml = SerializeToToml(original);

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_rt_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            var tomlPath = Path.Combine(tempDir, ProjectFileParser.FileName);
            File.WriteAllText(tomlPath, toml);

            var diagnostics = new DiagnosticBag();
            var parsed = ProjectFileParser.Parse(tempDir, diagnostics);

            if(parsed is null)
            {
                var diagMessages = string.Join("; ", diagnostics.ToImmutableArray().Select(d => d.Message));
                throw new Exception(
                    $"Parse returned null for valid config. TOML:\n{toml}\nDiagnostics: {diagMessages}");
            }

            if(parsed.Name != original.Name)
            {
                return false;
            }

            if(parsed.NamespaceRoot != original.NamespaceRoot)
            {
                return false;
            }

            if(parsed.References.Length != original.References.Length)
            {
                return false;
            }

            for(var i = 0; i < original.References.Length; i++)
            {
                if(parsed.References[i].Name != original.References[i].Name
                    || parsed.References[i].Kind != original.References[i].Kind)
                {
                    return false;
                }

                var parsedPath = parsed.References[i].Path;
                var originalPath = original.References[i].Path;

                // The parser stores each reference path fully qualified against the project folder.
                var expectedNormalized = Path.GetFullPath(
                    Path.IsPathRooted(originalPath)
                        ? originalPath
                        : Path.Combine(tempDir, originalPath));

                if(parsedPath != expectedNormalized)
                {
                    return false;
                }
            }

            if(parsed.GlobalUsings.Length != original.GlobalUsings.Length)
            {
                return false;
            }

            for(var i = 0; i < original.GlobalUsings.Length; i++)
            {
                if(parsed.GlobalUsings[i] != original.GlobalUsings[i])
                {
                    return false;
                }
            }

            return true;
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}