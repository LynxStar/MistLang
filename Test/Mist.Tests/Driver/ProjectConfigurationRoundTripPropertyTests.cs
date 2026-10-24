// Feature: named-references, Property 1: Round-trip with named references
// **Validates: Requirements 1.1, 1.2, 1.3, 5.1, 5.2, 7.1, 7.2, 7.3, 7.4**
//
// For any valid ProjectConfiguration with distinct reference names, serializing to TOML
// and parsing back produces a ProjectConfiguration where each reference has equal Name
// and Path fields.

using System.Collections.Immutable;
using System.Text;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for <see cref="ProjectConfiguration"/> round-trip
/// through TOML serialization and <see cref="ProjectFileParser.Parse"/>.
/// All tests are pure in-process — no process spawning.
/// </summary>
public sealed class ProjectConfigurationRoundTripPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a valid project name matching <c>^[a-zA-Z0-9\-_\.]+$</c> with max length 128.
    /// </summary>
    private static Gen<string> GenProjectName()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        return Gen.Choose(1, 20).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(validChars), len)
                .Select(chars => new string(chars)));
    }

    /// <summary>
    /// Generates a valid namespace identifier segment: starts with letter or underscore,
    /// followed by alphanumeric or underscore characters.
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        var startChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(0, 8).SelectMany(restLen =>
                Gen.ArrayOf(Gen.Elements(restChars), restLen)
                    .Select(rest => first + new string(rest))));
    }

    /// <summary>
    /// Generates a valid namespace string matching
    /// <c>^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$</c>.
    /// </summary>
    private static Gen<string> GenNamespace()
    {
        return Gen.Choose(1, 4).SelectMany(segmentCount =>
            Gen.ArrayOf(GenIdentifier(), segmentCount)
                .Select(segments => string.Join(".", segments)));
    }

    /// <summary>
    /// Generates a list of distinct valid namespace strings (0–10 entries).
    /// </summary>
    private static Gen<ImmutableArray<string>> GenGlobalUsings()
    {
        return Gen.Choose(0, 10).SelectMany(count =>
            Gen.ArrayOf(GenNamespace(), count + 5)
                .Select(namespaces =>
                {
                    // Deduplicate while preserving order, then take up to count
                    var distinct = namespaces.Distinct(StringComparer.Ordinal).Take(count).ToArray();
                    return distinct.ToImmutableArray();
                }));
    }

    /// <summary>
    /// Generates a valid <see cref="ProjectConfiguration"/> with random name,
    /// namespace root, references (with distinct names), and global usings.
    /// </summary>
    private static Gen<ProjectConfiguration> GenProjectConfiguration()
    {
        return GenProjectName().SelectMany(name =>
            GenProjectName().SelectMany(nsRoot =>
                Gen.Choose(0, 10).SelectMany(refCount =>
                    Gen.ArrayOf(GenProjectName(), refCount + 10).SelectMany(refNames =>
                        GenGlobalUsings().Select(globalUsings =>
                        {
                            // Deduplicate reference names while preserving order, then take up to refCount
                            var distinctNames = refNames
                                .Distinct(StringComparer.Ordinal)
                                .Take(refCount)
                                .ToArray();

                            // Build references with synthetic absolute paths, one kind after another
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

    // ═══════════════════════════════════════════════════════════════════════
    // Serialization
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Serializes a <see cref="ProjectConfiguration"/> to TOML format using the
    /// <c>[references]</c> named table syntax.
    /// </summary>
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

    // ═══════════════════════════════════════════════════════════════════════
    // Arbitrary
    // ═══════════════════════════════════════════════════════════════════════

    public static Arbitrary<ProjectConfiguration> ArbProjectConfiguration()
    {
        return GenProjectConfiguration().ToArbitrary();
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property test
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1: Round-trip with named references.
    /// For any valid ProjectConfiguration with distinct reference names, serializing
    /// to TOML and parsing back produces a ProjectConfiguration where each reference
    /// has equal Name and Path fields.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ProjectConfigurationRoundTripPropertyTests) })]
    public bool RoundTrip_SerializeAndParse_ProducesEqualConfiguration(ProjectConfiguration original)
    {
        var toml = SerializeToToml(original);

        // Write to a temp directory and parse back
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
                // Parse failed — this should not happen for valid configurations
                var diagMessages = string.Join("; ", diagnostics.ToImmutableArray().Select(d => d.Message));
                throw new Exception(
                    $"Parse returned null for valid config. TOML:\n{toml}\nDiagnostics: {diagMessages}");
            }

            // Assert structural equality
            if(parsed.Name != original.Name)
            {
                return false;
            }

            if(parsed.NamespaceRoot != original.NamespaceRoot)
            {
                return false;
            }

            // References: compare Name and Path
            if(parsed.References.Length != original.References.Length)
            {
                return false;
            }

            for(var i = 0; i < original.References.Length; i++)
            {
                // Compare Name and Kind fields (should round-trip exactly)
                if(parsed.References[i].Name != original.References[i].Name
                    || parsed.References[i].Kind != original.References[i].Kind)
                {
                    return false;
                }

                // The parser normalizes relative paths to absolute using the project folder.
                // Since we write absolute paths in serialization, they should round-trip.
                var parsedPath = parsed.References[i].Path;
                var originalPath = original.References[i].Path;

                // The parser appends mist.toml if not present, and normalizes.
                // Our serialized paths already end with mist.toml, so after normalization
                // they should match.
                var expectedNormalized = Path.GetFullPath(
                    Path.IsPathRooted(originalPath)
                        ? originalPath
                        : Path.Combine(tempDir, originalPath));

                if(parsedPath != expectedNormalized)
                {
                    return false;
                }
            }

            // GlobalUsings: order-sensitive comparison
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