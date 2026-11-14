using FsCheck;
using FsCheck.Fluent;
using Mist.Common.Diagnostics;
using Mist.Driver;
using Mist.Driver.Contracts;

namespace Mist.Tests.Driver;

public sealed class OutputTypeParsingValidationPropertyTests
{
    private static Gen<string> GenValidOutputType()
    {
        return Gen.Elements("executable", "library");
    }

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
                .Where(s => s is not "executable" and not "library"));

        return Gen.Frequency(
            (3, nearMisses),
            (4, randomStrings));
    }

    private static Gen<string> GenArbitraryOutputType()
    {
        return Gen.Frequency(
            (2, GenValidOutputType()),
            (5, GenInvalidOutputType()));
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

    private static bool VerifyOutputTypeValidation(string candidateValue)
    {
        if(candidateValue is null)
        {
            return true;
        }

        if(!CanRepresentInToml(candidateValue))
        {
            return true;
        }

        var isValid = candidateValue is "executable" or "library";

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
                if(result is null)
                {
                    return false;
                }

                var hasOutputTypeError = diags.Any(d =>
                    d.Number.Value == "MIST0059");
                if(hasOutputTypeError)
                {
                    return false;
                }

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
                var hasOutputTypeError = diags.Any(d =>
                    d.Number.Value == "MIST0059" &&
                    d.Severity == DiagnosticSeverity.Error);
                return hasOutputTypeError;
            }
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

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
                d.Number.Value == "MIST0059"));
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }
}