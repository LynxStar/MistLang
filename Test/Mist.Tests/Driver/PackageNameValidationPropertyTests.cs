// Feature: assembly-type-provider, Property 1: Package Name Validation Round-Trip
// **Validates: Requirements 1.2, 1.3, 1.8**
//
// For any string, the ProjectFileParser SHALL accept it as a valid package name
// if and only if it matches the regex [a-zA-Z0-9\-_\.]+  and does not exceed 128
// characters. All other strings SHALL produce an Error diagnostic.

using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for package name validation in <see cref="ProjectFileParser"/>.
/// Verifies that acceptance/rejection of package names matches the regex
/// <c>[a-zA-Z0-9\-_\.]+</c> and ≤128 characters.
/// All tests are pure in-process — no process spawning.
/// </summary>
public sealed class PackageNameValidationPropertyTests
{
    // The canonical regex from the spec and implementation
    private static readonly Regex ValidPackageNameRegex = new(@"^[a-zA-Z0-9\-_\.]+$");

    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates strings that are valid package names: 1–128 chars from [a-zA-Z0-9\-_\.].
    /// </summary>
    private static Gen<string> GenValidPackageName()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        return Gen.Choose(1, 128).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(validChars), len)
                .Select(chars => new string(chars)));
    }

    /// <summary>
    /// Generates strings that are invalid package names due to containing
    /// characters outside [a-zA-Z0-9\-_\.].
    /// </summary>
    private static Gen<string> GenInvalidByChars()
    {
        var invalidChars = " @#$%^&*()+=[]{}|:;'<>,?/!~`".ToCharArray();
        var validChars = "abcdefghijklmnopqrstuvwxyz0123456789-_.".ToCharArray();

        return Gen.Choose(1, 30).SelectMany(len =>
            Gen.Elements(invalidChars).SelectMany(badChar =>
                Gen.ArrayOf(Gen.Elements(validChars), len).Select(rest =>
                {
                    // Insert the bad char at a random position in the middle
                    var insertPos = rest.Length / 2;
                    var result = new char[rest.Length + 1];
                    Array.Copy(rest, 0, result, 0, insertPos);
                    result[insertPos] = badChar;
                    Array.Copy(rest, insertPos, result, insertPos + 1, rest.Length - insertPos);
                    return new string(result);
                })));
    }

    /// <summary>
    /// Generates strings that are invalid because they exceed 128 characters
    /// (but contain only valid characters).
    /// </summary>
    private static Gen<string> GenTooLong()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        return Gen.Choose(129, 200).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(validChars), len)
                .Select(chars => new string(chars)));
    }

    /// <summary>
    /// Generates arbitrary non-null strings (mix of valid and invalid package names).
    /// </summary>
    private static Gen<string> GenArbitraryPackageName()
    {
        return Gen.Frequency(
            (3, GenValidPackageName()),
            (2, GenInvalidByChars()),
            (2, GenTooLong()));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Determines whether a string should be accepted as a valid package name
    /// according to the spec: matches [a-zA-Z0-9\-_\.]+ and ≤128 chars.
    /// </summary>
    private static bool IsValidPackageName(string name)
    {
        if(string.IsNullOrEmpty(name))
        {
            return false;
        }

        if(name.Length > 128)
        {
            return false;
        }

        return ValidPackageNameRegex.IsMatch(name);
    }

    /// <summary>
    /// Escapes a string for use as a TOML quoted key value.
    /// </summary>
    private static string EscapeTomlString(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("\"", "\\\"")
            .Replace("\t", "\\t")
            .Replace("\n", "\\n")
            .Replace("\r", "\\r");
    }

    /// <summary>
    /// Determines whether a string can be represented as a TOML basic string key.
    /// TOML basic strings cannot contain certain control characters.
    /// </summary>
    private static bool CanRepresentInToml(string value)
    {
        foreach(var c in value)
        {
            if(c < 0x09)
            {
                return false;
            }

            if(c is (char)0x0B or (char)0x0C)
            {
                return false;
            }

            if(c is >= (char)0x0E and <= (char)0x1F)
            {
                return false;
            }

            if(c == 0x7F)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Verifies the property for a single candidate name.
    /// Returns true if the parser's acceptance/rejection matches the expected behavior.
    /// </summary>
    private static bool VerifyPackageNameValidation(string candidateName)
    {
        if(candidateName is null)
        {
            return true;
        }

        // Skip strings that cannot be represented in TOML (control characters)
        if(!CanRepresentInToml(candidateName))
        {
            return true;
        }

        var expectedValid = IsValidPackageName(candidateName);

        // Build a TOML document with the candidate as a package name (quoted key)
        var escapedName = EscapeTomlString(candidateName);
        var toml = $"name = \"TestProject\"\n\n[packages]\n\"{escapedName}\" = \"1.0.0\"\n";

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_pkg_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ProjectFileParser.FileName), toml);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(tempDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();

            if(expectedValid)
            {
                // Should parse successfully with no package-name errors
                if(result is null)
                {
                    return false;
                }

                var hasNameError = diags.Any(d =>
                    d.Number.Value == "MIST_PKG_INVALID_NAME");
                return !hasNameError;
            }
            else
            {
                // Should produce an error diagnostic for the invalid package name
                var hasNameError = diags.Any(d =>
                    d.Number.Value == "MIST_PKG_INVALID_NAME" &&
                    d.Severity == DiagnosticSeverity.Error);
                return hasNameError;
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property test
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 1: Package Name Validation Round-Trip.
    /// For any string that can be represented in TOML, the ProjectFileParser accepts it
    /// as a valid package name if and only if it matches [a-zA-Z0-9\-_\.]+ and ≤128 chars.
    /// Uses FsCheck Gen.Sample to generate 200 random inputs and verify the property.
    /// </summary>
    [Fact]
    public void PackageName_AcceptanceMatchesRegexAndLengthConstraint()
    {
        var samples = GenArbitraryPackageName().Sample(200).ToList();

        for(var i = 0; i < samples.Count; i++)
        {
            var candidateName = samples[i];
            var passed = VerifyPackageNameValidation(candidateName);

            if(!passed)
            {
                var expectedValid = IsValidPackageName(candidateName);
                throw new Exception(
                    $"Property failed on sample {i}/{samples.Count}.\n" +
                    $"  Input: \"{candidateName}\" (length={candidateName.Length})\n" +
                    $"  Expected valid: {expectedValid}\n" +
                    $"  Regex match: {ValidPackageNameRegex.IsMatch(candidateName)}\n" +
                    $"  Length ≤ 128: {candidateName.Length <= 128}");
            }
        }
    }
}