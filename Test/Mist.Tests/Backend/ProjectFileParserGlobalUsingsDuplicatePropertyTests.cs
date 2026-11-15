using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

public sealed class ProjectFileParserGlobalUsingsDuplicatePropertyTests
{
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

    private static Gen<string> GenIdentifier()
    {
        var startChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_".ToCharArray();
        var restChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(0, 7).SelectMany(extraLen =>
                Gen.ArrayOf(Gen.Elements(restChars), extraLen)
                    .Select(rest => first + new string(rest))));
    }

    private static Gen<string> GenValidNamespace()
    {
        return Gen.Choose(1, 3).SelectMany(segmentCount =>
            Gen.ArrayOf(GenIdentifier(), segmentCount)
                .Select(segments => string.Join(".", segments)));
    }

    private static Gen<string[]> GenNamespacesWithDuplicates()
    {
        return Gen.Choose(1, 6).SelectMany(uniqueCount =>
            Gen.ArrayOf(GenValidNamespace(), uniqueCount).SelectMany(baseNamespaces =>
                Gen.Choose(0, baseNamespaces.Length - 1).SelectMany(dupIdx =>
                    Gen.Choose(1, 3).Select(dupCount =>
                    {
                        var result = new List<string>(baseNamespaces);
                        for(var i = 0; i < dupCount; i++)
                        {
                            result.Add(baseNamespaces[dupIdx]);
                        }

                        return result.ToArray();
                    }))));
    }

    [Fact]
    public void P3_DuplicateDetectionAndRemoval_WarnsAndDeduplicatesPreservingOrder()
    {
        var gen = GenNamespacesWithDuplicates();

        RunProperty(gen, namespaces =>
        {
            var projectDir = CreateTempProjectDir();
            try
            {
                var namespacesLiteral = string.Join(", ", namespaces.Select(ns => $"\"{ns}\""));
                var toml = $"name = \"TestProject\"\n\n[global_usings]\nnamespaces = [{namespacesLiteral}]\n";

                File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

                var diagnostics = new DiagnosticBag();
                var result = ProjectFileParser.Parse(projectDir, diagnostics);

                Assert.NotNull(result);

                var diags = diagnostics.ToImmutableArray();

                var duplicateWarnings = diags
                    .Where(d => d.Number.Value == "MIST0082")
                    .ToList();
                Assert.NotEmpty(duplicateWarnings);

                foreach(var warning in duplicateWarnings)
                {
                    Assert.Equal(DiagnosticSeverity.Warning, warning.Severity);
                }

                var resultNamespaces = result.GlobalUsings;
                var distinctResult = resultNamespaces.Distinct().ToList();
                Assert.Equal(distinctResult.Count, resultNamespaces.Length);

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