using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.Common.Diagnostics;
using Mist.Driver;

namespace Mist.Tests.Backend;

public sealed class NamespaceRootResolutionPropertyTests
{
    private static readonly char[] ValidChars =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-_.".ToCharArray();

#pragma warning disable CA1034
    public record ValidName(string Value)
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

    private static string CreateTempProjectDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"mist_nsroot_{Guid.NewGuid():N}");
        _ = Directory.CreateDirectory(dir);
        return dir;
    }

    private static void WriteToml(string projectDir, string tomlContent)
    {
        File.WriteAllText(Path.Combine(projectDir, ProjectFileParser.FileName), tomlContent);
    }

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