// Feature: attributes-and-test-command, Property 4: Output Type Parsing Validation
// **Validates: Requirements 14.1, 14.2, 14.3**
//
// For any string value used as the output_type field in a valid mist.toml:
// - "executable" produces success with ProjectOutputType.Executable
// - "library" produces success with ProjectOutputType.Library
// - All other strings produce a diagnostic error containing MIST_PROJECT_INVALID_OUTPUT_TYPE

using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

/// <summary>
/// Property-based tests for <c>output_type</c> field validation in
/// <see cref="ProjectFileParser"/>. Verifies that only "executable" and "library"
/// are accepted, and all other strings produce an error diagnostic.
/// All tests are pure in-process -- no process spawning.
/// </summary>
public sealed class OutputTypeParsingValidationPropertyTests
{
    // ═══════════════════════════════════════════════════════════════════════
    // Generators
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates one of the two valid output_type values.
    /// </summary>
    private static Gen<string> GenValidOutputType()
    {
        return Gen.Elements("executable", "library");
    }

    /// <summary>
    /// Generates strings that are NOT "executable" or "library".
    /// Includes near-misses, case variants, empty strings, and random text.
    /// </summary>
    private static Gen<string> GenInvalidOutputType()
    {
        var nearMisses = Gen.Elements(
            "Executable", "Library", "EXECUTABLE", "LIBRARY",
            "exe", "lib", "dll", "exec", "libraries",
            "executables", "Lib", "Exe", " executable", "library ",
            "executable ", " library", "console", "winexe");

        var alphaChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_. ".ToCharArray();
        var randomStrings = Gen.Choose(1, 30).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(alphaChars), len)
                .Select(chars => new string(chars))
                .Where(s => s != "executable" && s != "library"));

        return Gen.Frequency(
            (3, nearMisses),
            (4, randomStrings));
    }

    /// <summary>
    /// Generates arbitrary output_type string values (mix of valid and invalid).
    /// </summary>
    private static Gen<string> GenArbitraryOutputType()
    {
        return Gen.Frequency(
            (2, GenValidOutputType()),
            (5, GenInvalidOutputType()));
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Escapes a string for use in a TOML basic string.
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
    /// Determines whether a string can be represented as a TOML basic string.
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
    /// Verifies the property for a single candidate output_type value.
    /// Returns true if the parser's acceptance/rejection matches the expected behavior.
    /// </summary>
    private static bool VerifyOutputTypeValidation(string candidateValue)
    {
        if(candidateValue is null)
        {
            return true;
        }

        // Skip strings that cannot be represented in TOML
        if(!CanRepresentInToml(candidateValue))
        {
            return true;
        }

        var isValid = candidateValue is "executable" or "library";

        // Build a minimal TOML document with the output_type field
        var escapedValue = EscapeTomlString(candidateValue);
        var toml = $"name = \"TestProject\"\noutput_type = \"{escapedValue}\"\n";

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_ot_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ProjectFileParser.FileName), toml);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(tempDir, diagnostics);

            var diags = diagnostics.ToImmutableArray();

            if(isValid)
            {
                // Should parse successfully with no output_type errors
                if(result is null)
                {
                    return false;
                }

                var hasOutputTypeError = diags.Any(d =>
                    d.Id.Value == "MIST_PROJECT_INVALID_OUTPUT_TYPE");
                if(hasOutputTypeError)
                {
                    return false;
                }

                // Verify the correct enum value
                if(candidateValue == "executable" && result.OutputType != ProjectOutputType.Executable)
                {
                    return false;
                }

                if(candidateValue == "library" && result.OutputType != ProjectOutputType.Library)
                {
                    return false;
                }

                return true;
            }
            else
            {
                // Should produce an error diagnostic for the invalid output type
                var hasOutputTypeError = diags.Any(d =>
                    d.Id.Value == "MIST_PROJECT_INVALID_OUTPUT_TYPE" &&
                    d.Severity == DiagnosticSeverity.Error);
                return hasOutputTypeError;
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    // ═══════════════════════════════════════════════════════════════════════
    // Property tests
    // ═══════════════════════════════════════════════════════════════════════

    /// <summary>
    /// Property 4: Output Type Parsing Validation.
    /// For any string value used as output_type: "executable" and "library" produce
    /// success with the correct enum value; all other strings produce a diagnostic
    /// error with MIST_PROJECT_INVALID_OUTPUT_TYPE.
    /// Uses FsCheck Gen.Sample to generate 200 random inputs and verify the property.
    /// </summary>
    [Fact]
    public void OutputType_AcceptanceMatchesValidValues()
    {
        var samples = GenArbitraryOutputType().Sample(200).ToList();

        for(var i = 0; i < samples.Count; i++)
        {
            var candidateValue = samples[i];
            var passed = VerifyOutputTypeValidation(candidateValue);

            if(!passed)
            {
                var isValid = candidateValue is "executable" or "library";
                throw new Exception(
                    $"Property failed on sample {i}/{samples.Count}.\n" +
                    $"  Input: \"{candidateValue}\"\n" +
                    $"  Expected valid: {isValid}");
            }
        }
    }

    /// <summary>
    /// Verifies that when output_type is absent, the parser defaults to Executable.
    /// </summary>
    [Fact]
    public void OutputType_AbsentDefaultsToExecutable()
    {
        var toml = "name = \"TestProject\"\n";

        var tempDir = Path.Combine(Path.GetTempPath(), $"mist_pbt_ot_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(tempDir);
        try
        {
            File.WriteAllText(Path.Combine(tempDir, ProjectFileParser.FileName), toml);

            var diagnostics = new DiagnosticBag();
            var result = ProjectFileParser.Parse(tempDir, diagnostics);

            Assert.NotNull(result);
            Assert.Equal(ProjectOutputType.Executable, result.OutputType);
            Assert.Empty(diagnostics.ToImmutableArray().Where(d =>
                d.Id.Value == "MIST_PROJECT_INVALID_OUTPUT_TYPE"));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}