// Feature: inferred-namespaces, Property 2: NamespaceRoot Validation
// For any string value provided as namespace_root in mist.toml, parsing succeeds
// iff the value is non-empty, non-whitespace, and contains only [a-zA-Z0-9\-_.].

using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for NamespaceRoot validation in <see cref="ProjectFileParser"/>.
/// </summary>
public sealed class NamespaceRootValidationPropertyTests
{
    private static readonly char[] ValidChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();

    private static readonly char[] WhitespaceChars = [' ', '\t', '\n', '\r'];

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidName(string Value)
    {
        public override string ToString() => Value;
    }

    public record WhitespaceOnly(string Value)
    {
        public override string ToString() => $"[whitespace len={Value.Length}]";
    }

    public record InvalidNsRoot(string Value)
    {
        public override string ToString() => Value;
    }
#pragma warning restore CA1034

    // ─── Arbitrary factories ─────────────────────────────────────────────

    public static Arbitrary<ValidName> ArbitraryValidName()
    {
        var gen = from len in Gen.Choose(1, 128)
                  from chars in Gen.ArrayOf(Gen.Elements(ValidChars), len)
                  select new ValidName(new string(chars));
        return gen.ToArbitrary();
    }

    public static Arbitrary<WhitespaceOnly> ArbitraryWhitespaceOnly()
    {
        var gen = from len in Gen.Choose(1, 64)
                  from chars in Gen.ArrayOf(Gen.Elements(WhitespaceChars), len)
                  select new WhitespaceOnly(new string(chars));
        return gen.ToArbitrary();
    }

    public static Arbitrary<InvalidNsRoot> ArbitraryInvalidNsRoot()
    {
        // Printable ASCII characters outside the valid set
        var invalidChars = Enumerable.Range(0x21, 0x7E - 0x21 + 1)
            .Select(i => (char)i)
            .Where(c => !ValidChars.Contains(c))
            .ToArray();

        var gen = from len in Gen.Choose(1, 127)
                  from validPart in Gen.ArrayOf(Gen.Elements(ValidChars), len)
                  from invalidChar in Gen.Elements(invalidChars)
                  from insertPos in Gen.Choose(0, len)
                  let combined = validPart.Take(insertPos)
                      .Append(invalidChar)
                      .Concat(validPart.Skip(insertPos))
                      .Take(128)
                      .ToArray()
                  select new InvalidNsRoot(new string(combined));
        return gen.ToArbitrary();
    }

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_nsval_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    private static string EscapeForToml(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\t", "\\t")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    // ─── Property 2a: Valid namespace_root values are accepted ───────────

    /// <summary>
    /// For any non-empty string of valid characters, namespace_root should be accepted.
    /// **Validates: Requirements 1.4, 1.5, 1.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceRootValidationPropertyTests) }, MaxTest = 100)]
    public bool ValidNamespaceRoot_IsAccepted(ValidName name, ValidName nsRoot)
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, $"name = \"{name.Value}\"\nnamespace_root = \"{nsRoot.Value}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            return result is not null
                && result.NamespaceRoot == nsRoot.Value
                && !diagnostics.HasErrors;
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Property 2b: Whitespace-only namespace_root is rejected ─────────

    /// <summary>
    /// For any whitespace-only string as namespace_root, parsing should fail
    /// with MIST_PROJECT_EMPTY_NAMESPACE_ROOT.
    /// **Validates: Requirements 1.4, 1.5, 1.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceRootValidationPropertyTests) }, MaxTest = 100)]
    public bool WhitespaceOnlyNamespaceRoot_IsRejected(ValidName name, WhitespaceOnly ws)
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var escaped = EscapeForToml(ws.Value);
            WriteToml(projectDir, $"name = \"{name.Value}\"\nnamespace_root = \"{escaped}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();
            return result is null
                && diags.Any(d => d.Id.Value == "MIST_PROJECT_EMPTY_NAMESPACE_ROOT");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

    // ─── Property 2c: Invalid character namespace_root is rejected ───────

    /// <summary>
    /// For any string with at least one invalid character as namespace_root,
    /// parsing should fail with MIST_PROJECT_INVALID_NAMESPACE_ROOT.
    /// **Validates: Requirements 1.4, 1.5, 1.6**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceRootValidationPropertyTests) }, MaxTest = 100)]
    public bool InvalidNamespaceRoot_IsRejected(ValidName name, InvalidNsRoot nsRoot)
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            var escaped = EscapeForToml(nsRoot.Value);
            WriteToml(projectDir, $"name = \"{name.Value}\"\nnamespace_root = \"{escaped}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();
            // Should be rejected with either INVALID_NAMESPACE_ROOT or EMPTY_NAMESPACE_ROOT
            // (if the string happens to be whitespace-only after escaping)
            return result is null
                && diags.Any(d => d.Id.Value == "MIST_PROJECT_INVALID_NAMESPACE_ROOT"
                               || d.Id.Value == "MIST_PROJECT_EMPTY_NAMESPACE_ROOT");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}