using System.Text.RegularExpressions;
using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Driver;

public sealed class PackageNameValidationPropertyTests
{
    // The canonical regex from the spec and implementation
    private static readonly Regex ValidPackageNameRegex = new(@"^[a-zA-Z0-9\-_\.]+$");

    private static Gen<string> GenValidPackageName()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        return Gen.Choose(1, 128).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(validChars), len)
                .Select(chars => new string(chars)));
    }

    private static Gen<string> GenInvalidByChars()
    {
        var invalidChars = " @#$%^&*()+=[]{}|:;'<>,?/!~`".ToCharArray();
        var validChars = "abcdefghijklmnopqrstuvwxyz0123456789-_.".ToCharArray();

        return Gen.Choose(1, 30).SelectMany(len =>
            Gen.Elements(invalidChars).SelectMany(badChar =>
                Gen.ArrayOf(Gen.Elements(validChars), len).Select(rest =>
                {
                    var insertPos = rest.Length / 2;
                    var result = new char[rest.Length + 1];
                    Array.Copy(rest, 0, result, 0, insertPos);
                    result[insertPos] = badChar;
                    Array.Copy(rest, insertPos, result, insertPos + 1, rest.Length - insertPos);
                    return new string(result);
                })));
    }

    private static Gen<string> GenTooLong()
    {
        var validChars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();
        return Gen.Choose(129, 200).SelectMany(len =>
            Gen.ArrayOf(Gen.Elements(validChars), len)
                .Select(chars => new string(chars)));
    }

    private static Gen<string> GenArbitraryPackageName()
    {
        return Gen.Frequency(
            (3, GenValidPackageName()),
            (2, GenInvalidByChars()),
            (2, GenTooLong()));
    }

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
                if(result is null)
                {
                    return false;
                }

                var hasNameError = diags.Any(d =>
                    d.Number.Value is "MIST0084" or "MIST0085");
                return !hasNameError;
            }
            else
            {
                var hasNameError = diags.Any(d =>
                    d.Number.Value is "MIST0084" or "MIST0085" &&
                    d.Severity == DiagnosticSeverity.Error);
                return hasNameError;
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

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