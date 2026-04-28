using FsCheck;
using FsCheck.Fluent;
using Storm.SourceGenerator.Emit;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Tests;

/// <summary>
/// Property-based tests for regex literal code emission.
/// Feature: regex-literals, Property 3: Emitter generates anchored compiled Regex code
/// </summary>
public class StormRegexLiteralEmitterPropertyTests
{
    private static Func<string, string?> InMemoryFileReader(Dictionary<string, string> files)
    {
        return path =>
        {
            var normalized = StormImportResolver.CanonicalizePath(path);
            if (files.TryGetValue(normalized, out var contents))
                return contents;
            return null;
        };
    }

    private static EmissionContext CreateContext(string grammar, string grammarName = "Test", string ns = "Test.Generated")
    {
        var files = new Dictionary<string, string>
        {
            ["root/main.storm"] = "grammar " + grammarName + ";\n" + grammar
        };

        var resolver = new StormImportResolver(InMemoryFileReader(files));
        var resolution = resolver.Resolve("root/main.storm");
        var stormGrammar = StormGrammar.Build(resolution);
        var loweringResult = GrammarIRLowering.Lower(stormGrammar);

        Assert.True(loweringResult.Succeeded, "Grammar lowering failed: " +
            string.Join(", ", loweringResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(loweringResult.Grammar);

        return new EmissionContext(ns, grammarName, loweringResult.Grammar!);
    }

    /// <summary>
    /// Generator for regex pattern strings that are safe to embed in a Storm grammar.
    /// Produces printable ASCII strings without unescaped backticks or double-quotes
    /// (which would break the verbatim string literal in generated C#).
    /// </summary>
    private static Gen<string> GenRegexPattern()
    {
        // Safe printable ASCII chars (exclude backtick and backslash for simplicity)
        var safeChars = Enumerable.Range(0x20, 0x7E - 0x20 + 1)
            .Select(i => (char)i)
            .Where(c => c != '`' && c != '\\')
            .ToArray();

        var safeCharGen = Gen.Elements(safeChars);

        // Escaped characters (backslash + any printable char)
        var escapedChars = Enumerable.Range(0x20, 0x7E - 0x20 + 1)
            .Select(i => (char)i)
            .ToArray();

        var escapeGen = Gen.Elements(escapedChars)
            .Select(c => "\\" + c);

        var segmentGen = Gen.Frequency(
            (8, safeCharGen.Select(c => c.ToString())),
            (2, escapeGen)
        );

        return segmentGen.ListOf()
            .Select(segments => string.Concat(segments))
            .Where(s => s.Length > 0); // non-empty patterns
    }

    /// <summary>
    /// **Validates: Requirements 6.1, 6.2, 6.3, 6.5**
    ///
    /// Property 3: Emitter generates anchored compiled Regex code.
    /// For any RegexLiteralIR with pattern p, the generated C# code SHALL contain
    /// RegexOptions.Compiled, the pattern anchored with ^, and a return expression
    /// yielding match length or 0.
    /// </summary>
    [Fact]
    public void Emitter_GeneratesAnchoredCompiledRegexCode()
    {
        var patterns = GenRegexPattern().Sample(100);
        foreach (var pattern in patterns)
        {
            var grammarSource = $"token T = `{pattern}`;\nrule Start = T;";

            var ctx = CreateContext(grammarSource);
            var output = LexerEmitter.Emit(ctx);

            // Must contain using directive for System.Text.RegularExpressions
            Assert.True(output.Contains("using System.Text.RegularExpressions;"),
                $"Missing using directive for pattern: {pattern}");

            // Must contain RegexOptions.Compiled
            Assert.True(output.Contains("RegexOptions.Compiled"),
                $"Missing RegexOptions.Compiled for pattern: {pattern}");

            // Must contain anchored pattern with ^ and non-capturing group
            var escapedPattern = pattern.Replace("\"", "\"\"");
            Assert.True(output.Contains($"^(?:{escapedPattern})"),
                $"Missing anchored pattern ^(?:{escapedPattern}) for pattern: {pattern}");

            // Must contain match length or 0 return expression
            Assert.True(output.Contains(".Length : 0"),
                $"Missing .Length : 0 return expression for pattern: {pattern}");

            // Must contain .Match(input.Substring( to verify position-anchored matching
            Assert.True(output.Contains(".Match(input.Substring("),
                $"Missing .Match(input.Substring( call for pattern: {pattern}");
        }
    }
}
