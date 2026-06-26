// Feature: global-usings, Property 2: Invalid namespace rejection
// Feature: global-usings, Property 4: Diagnostic file path inclusion
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for <see cref="ProjectFileParser"/> global usings validation.
/// All tests are pure in-process — no process spawning.
///
/// **Validates: Requirements 2.1, 2.2**
/// </summary>
public sealed class ProjectFileParserGlobalUsingsPropertyTests
{
    // ── Helpers ───────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_pbt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
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

    // ── Generators ───────────────────────────────────────────────────

    /// <summary>
    /// Generates empty strings (length 0).
    /// </summary>
    private static Gen<string> GenEmptyString()
    {
        return Gen.Constant(string.Empty);
    }

    /// <summary>
    /// Generates whitespace-only strings: 1–20 space characters.
    /// Uses only spaces to avoid TOML parsing issues with control characters.
    /// </summary>
    private static Gen<string> GenWhitespaceOnly()
    {
        return Gen.Choose(1, 20)
            .Select(len => new string(' ', len));
    }

    /// <summary>
    /// Generates strings containing invalid characters — characters outside
    /// the valid namespace pattern <c>^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$</c>.
    /// These strings are non-empty, non-whitespace-only, and contain at least one
    /// character that makes the namespace invalid.
    /// Only uses characters that are safe inside a TOML basic string literal.
    /// </summary>
    private static Gen<string> GenInvalidCharacterString()
    {
        // Characters that are invalid in a namespace identifier but safe in TOML basic strings.
        // Excluded: backslash, double-quote, control chars, and brackets/braces that could
        // break TOML array syntax when not properly escaped.
        var invalidChars = new[] { '!', '@', '#', '$', '%', '^', '&', '*', '(', ')', '-', '+', '=', '~', ':', ';', '<', '>', '?' };
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.".ToCharArray();

        // Strategy: generate a string with a valid prefix, then inject an invalid char
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

    /// <summary>
    /// Generates strings that start with a digit (invalid start for namespace segments).
    /// Pattern requires segments to start with [a-zA-Z_].
    /// </summary>
    private static Gen<string> GenDigitStartString()
    {
        var digits = "0123456789".ToCharArray();
        var validTail = "abcdefghijklmnopqrstuvwxyz0123456789_".ToCharArray();

        return Gen.Elements(digits).SelectMany(digit =>
            Gen.Choose(0, 8).SelectMany(tailLen =>
                Gen.ArrayOf(Gen.Elements(validTail), tailLen)
                    .Select(tail => digit + new string(tail))));
    }

    /// <summary>
    /// Generates strings with dot-separated segments where at least one segment
    /// starts with a digit (e.g., "Valid.3invalid").
    /// </summary>
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

    /// <summary>
    /// Combined generator that produces invalid namespace strings from all categories:
    /// empty, whitespace-only, contains invalid characters, starts with digit,
    /// or has a segment starting with a digit.
    /// </summary>
    private static Gen<string> GenInvalidNamespace()
    {
        return Gen.OneOf(
            GenEmptyString(),
            GenWhitespaceOnly(),
            GenInvalidCharacterString(),
            GenDigitStartString(),
            GenInvalidSegmentStart());
    }

    // ── Property 2: Invalid namespace rejection ─────────────────────
    // Feature: global-usings, Property 2: Invalid namespace rejection

    /// <summary>
    /// Property 2: For any string that is either empty, whitespace-only, or contains
    /// characters outside the valid namespace pattern, parsing a mist.toml with that
    /// string in the namespaces array SHALL produce an error diagnostic (either
    /// MIST_GLOBAL_USING_EMPTY_NAMESPACE or MIST_GLOBAL_USING_INVALID_NAMESPACE)
    /// and return null.
    ///
    /// **Validates: Requirements 2.1, 2.2**
    /// </summary>
    [Fact]
    public void P2_InvalidNamespaces_AreRejectedWithCorrectDiagnostic()
    {
        RunProperty(GenInvalidNamespace(), invalidNs =>
        {
            var projectDir = CreateTempProjectDir();
            try
            {
                // Escape the string for TOML: replace backslashes and quotes
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
                    "MIST_GLOBAL_USING_EMPTY_NAMESPACE",
                    "MIST_GLOBAL_USING_INVALID_NAMESPACE"
                };

                Assert.Contains(relevantDiag.Id.Value, validIds);
                Assert.Equal(DiagnosticSeverity.Error, relevantDiag.Severity);
            }
            finally
            {
                Directory.Delete(projectDir, recursive: true);
            }
        });
    }

    // ── Generators for Property 4 ───────────────────────────────────

    /// <summary>
    /// Generates a valid namespace string matching the pattern
    /// <c>^[a-zA-Z_][a-zA-Z0-9_]*(\.[a-zA-Z_][a-zA-Z0-9_]*)*$</c>.
    /// </summary>
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

    /// <summary>
    /// Represents a category of invalid global_usings configuration that produces diagnostics.
    /// </summary>
    private enum InvalidConfigKind
    {
        EmptyNamespace,
        WhitespaceNamespace,
        InvalidCharacterNamespace,
        DuplicateNamespace
    }

    /// <summary>
    /// Generates a random invalid [global_usings] configuration along with its TOML content.
    /// Each configuration is guaranteed to produce at least one diagnostic.
    /// </summary>
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

    // ── Property 4: Diagnostic file path inclusion ──────────────────
    // Feature: global-usings, Property 4: Diagnostic file path inclusion

    /// <summary>
    /// Property 4: For any invalid [global_usings] configuration that produces a
    /// diagnostic, the diagnostic message SHALL contain the absolute path to the
    /// mist.toml file.
    ///
    /// **Validates: Requirements 2.6**
    /// </summary>
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
                ProjectFileParser.Parse(projectDir, diagnostics);

                var diags = diagnostics.ToImmutableArray();
                Assert.NotEmpty(diags);

                var expectedPath = Path.Combine(projectDir, "mist.toml");

                // ALL produced diagnostics must contain the file path
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