using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

public sealed class ProjectFileParserPropertyTests
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

    private static Gen<string> GenPathSegment()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyz0123456789-_".ToCharArray();
        var startChars = "abcdefghijklmnopqrstuvwxyz".ToCharArray();

        return Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(0, 11).SelectMany(extraLen =>
                Gen.ArrayOf(Gen.Elements(validChars), extraLen)
                    .Select(rest => first + new string(rest))));
    }

    private static Gen<string> GenRelativePath()
    {
        return Gen.Choose(1, 3).SelectMany(depth =>
            Gen.ArrayOf(GenPathSegment(), depth)
                .Select(segments => string.Join("/", segments)));
    }

    [Fact]
    public void P1_RoundTrip_ReferencePaths_PreservedAfterParsing()
    {
        var gen = Gen.Choose(1, 5).SelectMany(count =>
            Gen.ArrayOf(GenRelativePath(), count)
                .Select(paths => paths.ToList()));

        RunProperty(gen, paths =>
        {
            var projectDir = CreateTempProjectDir();
            try
            {
                var toml = "name = \"TestProject\"\n\n[references]\n";
                for(var i = 0; i < paths.Count; i++)
                {
                    toml += $"Ref{i} = {{ path = \"{paths[i]}\" }}\n";
                }

                File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

                var diagnostics = new DiagnosticBag();
                var result = ProjectFileParser.Parse(projectDir, diagnostics);

                Assert.NotNull(result);
                Assert.False(diagnostics.HasErrors,
                    $"Parse reported errors: {string.Join("; ", diagnostics.ToImmutableArray().Select(d => d.Message))}");
                Assert.Equal(paths.Count, result.References.Length);

                for(var i = 0; i < paths.Count; i++)
                {
                    var expected = Path.GetFullPath(Path.Combine(projectDir, paths[i]));
                    if(!Path.GetFileName(expected).Equals("mist.toml", StringComparison.Ordinal))
                    {
                        expected = Path.Combine(expected, "mist.toml");
                    }

                    Assert.Equal(expected, result.References[i].Path);
                }
            }
            finally
            {
                Directory.Delete(projectDir, recursive: true);
            }
        });
    }

    [Fact]
    public void P2_WhitespacePaths_AreRejected()
    {
        // Spaces only: a tab is tricky to escape in TOML, and a space is unambiguously whitespace.
        var gen = Gen.Choose(1, 64)
            .Select(len => new string(' ', len));

        RunProperty(gen, wsPath =>
        {
            var projectDir = CreateTempProjectDir();
            try
            {
                var toml = $"name = \"TestProject\"\n\n[references]\nBadRef = {{ path = \"{wsPath}\" }}\n";
                File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

                var diagnostics = new DiagnosticBag();
                var result = ProjectFileParser.Parse(projectDir, diagnostics);

                Assert.Null(result);
                var diags = diagnostics.ToImmutableArray();
                _ = Assert.Single(diags);
                Assert.Equal("MIST0061", diags[0].Number.Value);
                Assert.Contains("mist.toml", diags[0].Message);
            }
            finally
            {
                Directory.Delete(projectDir, recursive: true);
            }
        });
    }

    [Fact]
    public void P3_UnknownFields_AreIgnored()
    {
        var gen = GenRelativePath().SelectMany(path =>
            Gen.Choose(1, 4).SelectMany(fieldCount =>
                Gen.ArrayOf(GenPathSegment(), fieldCount).SelectMany(fieldNames =>
                    Gen.ArrayOf(GenPathSegment(), fieldCount)
                        .Select(fieldValues => (
                            path,
                            fields: fieldNames.Zip(fieldValues).ToList())))));

        RunProperty(gen, input =>
        {
            var (path, fields) = input;
            var projectDir = CreateTempProjectDir();
            try
            {
                var toml = "name = \"TestProject\"\n\n[references.MyRef]\n";
                toml += $"path = \"{path}\"\n";
                var seenKeys = new HashSet<string>();
                foreach(var (key, value) in fields)
                {
                    // Ensure key doesn't collide with "path" or "name"
                    var safeKey = "extra_" + key;
                    // TOML rejects a duplicate key.
                    if(!seenKeys.Add(safeKey))
                    {
                        continue;
                    }

                    toml += $"{safeKey} = \"{value}\"\n";
                }

                File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

                var diagnostics = new DiagnosticBag();
                var result = ProjectFileParser.Parse(projectDir, diagnostics);

                Assert.False(diagnostics.HasErrors,
                    $"Parse reported errors for path='{path}', fields=[{string.Join(", ", fields.Select(f => $"{f.First}={f.Second}"))}], toml:\n{toml}\n" +
                    string.Join("; ", diagnostics.ToImmutableArray().Select(d => $"{d.Number.Value}: {d.Message}")));
                Assert.NotNull(result);
                _ = Assert.Single(result.References);

                var expected = Path.GetFullPath(Path.Combine(projectDir, path));
                if(!Path.GetFileName(expected).Equals("mist.toml", StringComparison.Ordinal))
                {
                    expected = Path.Combine(expected, "mist.toml");
                }

                Assert.Equal(expected, result.References[0].Path);
            }
            finally
            {
                Directory.Delete(projectDir, recursive: true);
            }
        });
    }
}