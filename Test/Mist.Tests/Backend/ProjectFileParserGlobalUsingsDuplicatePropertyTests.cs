// Feature: global-usings, Property 3: Duplicate detection and removal
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for <see cref="ProjectFileParser"/> global usings duplicate detection.
/// All tests are pure in-process — no process spawning.
///
/// **Validates: Requirements 2.5**
/// </summary>
public sealed class ProjectFileParserGlobalUsingsDuplicatePropertyTests
{
    // ── Helpers ───────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private static void RunProperty<T>(Gen<T> gen, Action<T> check, int maxTests = 100)
    {
        var samples = gen.Sample(maxTests).ToList();
        for(var idx = 0; idx < samples.Count; idx++)
        {
            try
            {
                check(samples[idx]);
            }
            catch(Exception ex)
            {
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: {samples[idx]}\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    // ── Generators ───────────────────────────────────────────────────

    /// <summary>
    /// Generates a valid namespace identifier segment: starts with a letter or underscore,
    /// followed by 0–7 alphanumeric/underscore characters.
    /// Matches the regex: [a-zA-Z_][a-zA-Z0-9_]*
    /// </summary>
    private static Gen<string> GenIdentifier()
    {
        var startChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(0, 7).SelectMany(extraLen =>
                Gen.ArrayOf(Gen.Elements(restChars), extraLen)
                    .Select(rest => first + new string(rest))));
    }

    /// <summary>
    /// Generates a valid namespace string: 1–3 dot-separated identifier segments.
    /// Matches the regex: ^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$
    /// </summary>
    private static Gen<string> GenValidNamespace()
    {
        return Gen.Choose(1, 3).SelectMany(segmentCount =>
            Gen.ArrayOf(GenIdentifier(), segmentCount)
                .Select(segments => string.Join(".", segments)));
    }

    /// <summary>
    /// Generates a namespace array with at least one duplicate injected.
    /// Strategy: generate 1–6 unique namespaces, then append 1–3 copies of a
    /// randomly chosen entry to guarantee at least one duplicate exists.
    /// </summary>
    private static Gen<string[]> GenNamespacesWithDuplicates()
    {
        return Gen.Choose(1, 6).SelectMany(uniqueCount =>
            Gen.ArrayOf(GenValidNamespace(), uniqueCount).SelectMany(baseNamespaces =>
                Gen.Choose(0, baseNamespaces.Length - 1).SelectMany(dupIdx =>
                    Gen.Choose(1, 3).Select(dupCount =>
                    {
                        // Start with the base namespaces, then append duplicates
                        var result = new List<string>(baseNamespaces);
                        for(var i = 0; i < dupCount; i++)
                        {
                            result.Add(baseNamespaces[dupIdx]);
                        }

                        return result.ToArray();
                    }))));
    }

    // ── Property 3: Duplicate detection and removal ─────────────────
    // Feature: global-usings, Property 3: Duplicate detection and removal

    /// <summary>
    /// Property 3: For any namespaces array containing at least one duplicate entry
    /// (case-sensitive), parsing SHALL produce a MIST_GLOBAL_USING_DUPLICATE_NAMESPACE
    /// warning diagnostic and the resulting GlobalUsings list SHALL contain each namespace
    /// at most once, preserving the order of first occurrence.
    ///
    /// **Validates: Requirements 2.5**
    /// </summary>
    [Fact]
    public void P3_DuplicateDetectionAndRemoval_WarnsAndDeduplicatesPreservingOrder()
    {
        var gen = GenNamespacesWithDuplicates();

        RunProperty(gen, namespaces =>
        {
            var projectDir = CreateTempProjectDir();
            try
            {
                // Build mist.toml with the namespace array
                var namespacesLiteral = string.Join(", ", namespaces.Select(ns => $"\"{ns}\""));
                var toml = $"name = \"TestProject\"\n\n[global_usings]\nnamespaces = [{namespacesLiteral}]\n";

                File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

                var diagnostics = new DiagnosticBag();
                var result = ProjectFileParser.Parse(projectDir, diagnostics);

                // Result should NOT be null — duplicates produce warnings, not errors
                Assert.NotNull(result);

                var diags = diagnostics.ToImmutableArray();

                // Assert: at least one MIST_GLOBAL_USING_DUPLICATE_NAMESPACE warning is produced
                var duplicateWarnings = diags
                    .Where(d => d.Id.Value == "MIST_GLOBAL_USING_DUPLICATE_NAMESPACE")
                    .ToList();
                Assert.NotEmpty(duplicateWarnings);

                // Assert: all duplicate diagnostics are warnings (not errors)
                foreach(var warning in duplicateWarnings)
                {
                    Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
                }

                // Assert: resulting GlobalUsings contains each namespace at most once
                var resultNamespaces = result.GlobalUsings;
                var distinctResult = resultNamespaces.Distinct().ToList();
                Assert.Equal(distinctResult.Count, resultNamespaces.Length);

                // Assert: order preserves first-occurrence order
                var expectedOrder = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach(var ns in namespaces)
                {
                    if(seen.Add(ns))
                    {
                        expectedOrder.Add(ns);
                    }
                }

                Assert.Equal(expectedOrder.Count, resultNamespaces.Length);
                for(var i = 0; i < expectedOrder.Count; i++)
                {
                    Assert.Equal(expectedOrder[i], resultNamespaces[i]);
                }
            }
            finally
            {
                Directory.Delete(projectDir, recursive: true);
            }
        });
    }
}