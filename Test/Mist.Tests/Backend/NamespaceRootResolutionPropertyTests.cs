// Feature: inferred-namespaces, Property 1: NamespaceRoot Resolution
// For any valid mist.toml with a name field and an optional namespace_root field,
// the resolved NamespaceRoot equals namespace_root when present, name when absent.

using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

/// <summary>
/// Property-based tests for NamespaceRoot resolution in <see cref="ProjectFileParser"/>.
/// </summary>
public sealed class NamespaceRootResolutionPropertyTests
{
    private static readonly char[] ValidChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();

    // ─── Wrapper types for FsCheck ───────────────────────────────────────

#pragma warning disable CA1034
    public record ValidName(string Value)
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

    // ─── Helpers ─────────────────────────────────────────────────────────

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_nsroot_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

    // ─── Property 1a: When namespace_root is present, NamespaceRoot equals namespace_root ─

    /// <summary>
    /// For any valid name and valid namespace_root, the resolved NamespaceRoot
    /// equals the namespace_root value.
    /// **Validates: Requirements 1.2, 1.3, 12.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceRootResolutionPropertyTests) }, MaxTest = 100)]
    public bool WhenNamespaceRootPresent_ResolvesToNamespaceRoot(ValidName name, ValidName nsRoot)
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

    // ─── Property 1b: When namespace_root is absent, NamespaceRoot defaults to name ──

    /// <summary>
    /// For any valid name with no namespace_root field, the resolved NamespaceRoot
    /// equals the name value.
    /// **Validates: Requirements 1.2, 1.3, 12.1**
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NamespaceRootResolutionPropertyTests) }, MaxTest = 100)]
    public bool WhenNamespaceRootAbsent_DefaultsToName(ValidName name)
    {
        var projectDir = CreateTempProjectDir();
        try
        {
            WriteToml(projectDir, $"name = \"{name.Value}\"");

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(projectDir, diagnostics);

            return result is not null
                && result.NamespaceRoot == name.Value
                && !diagnostics.HasErrors;
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}