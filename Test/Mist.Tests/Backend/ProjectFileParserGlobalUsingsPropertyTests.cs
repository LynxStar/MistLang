using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

public sealed class ProjectFileParserGlobalUsingsPropertyTests
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
                var sampleStr = samples[idx]?.ToString() ?? "<null>";
                var escaped = string.Join("", sampleStr.Select(c => c < 32 ? $"\\x{(int)c:X2}" : c.ToString()));
                throw new Exception(
                    $"Property failed on sample {idx}/{samples.Count}: [{escaped}] (length={sampleStr.Length})\n" +
                    $"Inner: {ex.Message}", ex);
            }
        }
    }

    private static Gen<string> GenEmptyString()
    {
        return Gen.Constant(string.Empty);
    }

    /// <summary>
    /// Spaces only, avoiding TOML parsing issues with control characters.
    /// </summary>
    private static Gen<string> GenWhitespaceOnly()
    {
        return Gen.Choose(1, 20)
            .Select(len => new string(' ', len));
    }

    private static Gen<string> GenInvalidCharacterString()
    {
        // Invalid in a namespace yet safe in a TOML basic string: no backslash, double quote, control
        // character, bracket or brace.
        var invalidChars = new[] { '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '-', '+', '=', '~', ':', ';', '<', '>', '?' };
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.".ToCharArray();

        return Gen.Elements(invalidChars).SelectMany(invalidChar =>
            Gen.Choose(0, 8).SelectMany(prefixLen =>
                Gen.ArrayOf(Gen.Elements(validChars), prefixLen).SelectMany(prefix =>
                    Gen.Choose(0, 5).SelectMany(suffixLen =>
                        Gen.ArrayOf(Gen.Elements(validChars), suffixLen)
                            .Select(suffix =>
                            {
                                var result = new string(prefix) + invalidChar + new string(suffix);
                                return result;
                            })))));
    }

    private static Gen<string> GenDigitStartString()
    {
        var digits = "0123456789".ToCharArray();
        var validTail = "abcdefghijklmnopqrstuvwxyz0123456789_".ToCharArray();

        return Gen.Elements(digits).SelectMany(digit =>
            Gen.Choose(0, 8).SelectMany(tailLen =>
                Gen.ArrayOf(Gen.Elements(validTail), tailLen)
                    .Select(tail => digit + new string(tail))));
    }

    private static Gen<string> GenInvalidSegmentStart()
    {
        var validStart = "abcdefghijklmnopqrstuvwxyz_".ToCharArray();
        var validTail = "abcdefghijklmnopqrstuvwxyz0123456789_".ToCharArray();
        var digits = "0123456789".ToCharArray();

        var genValidSegment = Gen.Elements(validStart).SelectMany(first =>
            Gen.Choose(1, 6).SelectMany(tailLen =>
                Gen.ArrayOf(Gen.Elements(validTail), tailLen)
                    .Select(tail => first + new string(tail))));

        var genInvalidSegment = Gen.Elements(digits).SelectMany(digit =>
            Gen.Choose(1, 6).SelectMany(tailLen =>
                Gen.ArrayOf(Gen.Elements(validTail), tailLen)
                    .Select(tail => digit + new string(tail))));

        return genValidSegment.SelectMany(validSeg =>
            genInvalidSegment.Select(invalidSeg => validSeg + "." + invalidSeg));
    }

    private static Gen<string> GenInvalidNamespace()
    {
        return Gen.OneOf(
            GenEmptyString(),
            GenWhitespaceOnly(),
            GenInvalidCharacterString(),
            GenDigitStartString(),
            GenInvalidSegmentStart());
    }

    [Fact]
    public void P2_InvalidNamespaces_AreRejectedWithCorrectDiagnostic()
    {
        RunProperty(GenInvalidNamespace(), invalidNs =>
        {
            var projectDir = CreateTempProjectDir();
            try
            {
                var escaped = invalidNs
                    .Replace("\\", "\\\\")
                    .Replace("\"", "\\\"");

                var toml = $"name = \"TestProject\"\n\n[global_usings]\nnamespaces = [\"{escaped}\"]\n";
                File.WriteAllText(Path.Combine(projectDir, "mist.toml"), toml);

                var diagnostics = new DiagnosticBag();
                var result = ProjectFileParser.Parse(projectDir, diagnostics);

                Assert.Null(result);

                var diags = diagnostics.ToImmutableArray();
                Assert.NotEmpty(diags);

                // The diagnostic should be either EMPTY_NAMESPACE or INVALID_NAMESPACE
                var relevantDiag = diags[0];
                var validIds = new[]
                {
                    "MIST0080",
                    "MIST0081"
                };

                Assert.Contains(relevantDiag.Number.Value, validIds);
                Assert.Equal(DiagnosticSeverity.Error, relevantDiag.Severity);
            }
            finally
            {
                Directory.Delete(projectDir, recursive: true);
            }
        });
    }

    private static Gen<string> GenValidNamespace()
    {
        var startChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_".ToCharArray();
        var tailChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_".ToCharArray();

        var genSegment = Gen.Elements(startChars).SelectMany(first =>
            Gen.Choose(0, 6).SelectMany(tailLen =>
                Gen.ArrayOf(Gen.Elements(tailChars), tailLen)
                    .Select(tail => first + new string(tail))));

        return Gen.Choose(1, 3).SelectMany(segCount =>
            Gen.ArrayOf(genSegment, segCount)
                .Select(segs => string.Join(".", segs)));
    }

    private enum InvalidConfigKind
    {
        EmptyNamespace,
        WhitespaceNamespace,
        InvalidCharacterNamespace,
        DuplicateNamespace
    }

    private static Gen<(InvalidConfigKind Kind, string TomlContent)> GenInvalidGlobalUsingsConfig()
    {
        var genEmpty = Gen.Constant((InvalidConfigKind.EmptyNamespace,
            "name = \"TestProject\"\n\n[global_usings]\nnamespaces = [\"\"]\n"));

        var genWhitespace = GenWhitespaceOnly().Select(ws =>
        {
            var escaped = ws.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return (InvalidConfigKind.WhitespaceNamespace,
                $"name = \"TestProject\"\n\n[global_usings]\nnamespaces = [\"{escaped}\"]\n");
        });

        var genInvalidChar = GenInvalidCharacterString().Select(invalid =>
        {
            var escaped = invalid.Replace("\\", "\\\\").Replace("\"", "\\\"");
            return (InvalidConfigKind.InvalidCharacterNamespace,
                $"name = \"TestProject\"\n\n[global_usings]\nnamespaces = [\"{escaped}\"]\n");
        });

        var genDuplicate = GenValidNamespace().Select(ns =>
            (InvalidConfigKind.DuplicateNamespace,
                $"name = \"TestProject\"\n\n[global_usings]\nnamespaces = [\"{ns}\", \"{ns}\"]\n"));

        return Gen.OneOf(genEmpty, genWhitespace, genInvalidChar, genDuplicate);
    }

    [Fact]
    public void P4_AllDiagnostics_ContainMistTomlFilePath()
    {
        RunProperty(GenInvalidGlobalUsingsConfig(), config =>
        {
            var (kind, tomlContent) = config;
            var projectDir = CreateTempProjectDir();
            try
            {
                File.WriteAllText(Path.Combine(projectDir, "mist.toml"), tomlContent);

                var diagnostics = new DiagnosticBag();
                _ = ProjectFileParser.Parse(projectDir, diagnostics);

                var diags = diagnostics.ToImmutableArray();
                Assert.NotEmpty(diags);

                var expectedPath = Path.Combine(projectDir, "mist.toml");

                foreach(var diag in diags)
                {
                    Assert.Contains(expectedPath, diag.Message);
                }
            }
            finally
            {
                Directory.Delete(projectDir, recursive: true);
            }
        });
    }
}