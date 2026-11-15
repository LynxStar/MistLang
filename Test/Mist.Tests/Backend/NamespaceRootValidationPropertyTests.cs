using System.Collections.Immutable;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

public sealed class NamespaceRootValidationPropertyTests
{
    private static readonly char[] ValidChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();

    private static readonly char[] WhitespaceChars = [' ', '\t', '\n', '\r'];

#pragma warning disable CA1034
    public record ValidName(string Value)
    {
        public override string ToString()
        {
            return Value;
        }
    }

    public record WhitespaceOnly(string Value)
    {
        public override string ToString()
        {
            return $"[whitespace len={Value.Length}]";
        }
    }

    public record InvalidNsRoot(string Value)
    {
        public override string ToString()
        {
            return Value;
        }
    }
#pragma warning restore CA1034

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

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_nsval_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(dir);
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
                && diags.Any(d => d.Number.Value == "MIST0056");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }

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
            // MIST0056 covers a value that is whitespace-only once escaped.
            return result is null
                && diags.Any(d => d.Number.Value is "MIST0057"
                               or "MIST0056");
        }
        finally
        {
            Directory.Delete(projectDir, recursive: true);
        }
    }
}