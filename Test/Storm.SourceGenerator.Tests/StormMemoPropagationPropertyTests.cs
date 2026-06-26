using FsCheck;
using FsCheck.Fluent;
using Storm.SourceGenerator.Diagnostics;
using Storm.SourceGenerator.Frontend;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Tests;

/// <summary>
/// Property-based tests for memo annotation propagation through lowering.
/// Feature: parser-memoization-expansion, Property 3: Memo annotation propagation
/// </summary>
public class StormMemoPropagationPropertyTests
{
    // ══════════════════════════════════════════════════════════════════
    //  Generators
    // ══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Generates a valid Storm rule name (PascalCase identifier).
    /// </summary>
    private static Gen<string> GenRuleName()
    {
        var upperChars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ".ToCharArray();
        var lowerChars = "abcdefghijklmnopqrstuvwxyz".ToCharArray();

        return Gen.Elements(upperChars).SelectMany(first =>
            Gen.Choose(1, 8).SelectMany(len =>
                Gen.ArrayOf(Gen.Elements(lowerChars), len)
                    .Select(tail => first + new string(tail))));
    }

    /// <summary>
    /// Generates a list of (ruleName, isMemo) pairs representing rule declarations.
    /// Ensures unique names and at least one token exists for referencing.
    /// </summary>
    private static Gen<List<(string Name, bool IsMemo)>> GenRuleDeclarations()
    {
        return Gen.Choose(1, 8).SelectMany(count =>
            Gen.ListOf(
                GenRuleName().SelectMany(name =>
                    Gen.Elements(true, false).Select(isMemo => (name, isMemo))),
                count
            ).Select(rules =>
            {
                // Deduplicate rule names — keep first occurrence
                var seen = new HashSet<string>(StringComparer.Ordinal);
                var unique = new List<(string Name, bool IsMemo)>();
                foreach(var r in rules)
                {
                    if(seen.Add(r.name))
                    {
                        unique.Add((r.name, r.isMemo));
                    }
                }
                return unique;
            }));
    }

    /// <summary>
    /// Builds a complete Storm grammar source string from rule declarations.
    /// Each rule has a simple body referencing a shared token.
    /// </summary>
    private static string BuildGrammarSource(List<(string Name, bool IsMemo)> rules)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("grammar Test;");
        sb.AppendLine("token Tok = [a-z]+;");

        foreach(var (name, isMemo) in rules)
        {
            if(isMemo)
            {
                sb.AppendLine($"@memo");
            }
            sb.AppendLine($"rule {name} = Tok;");
        }

        return sb.ToString();
    }

    // ══════════════════════════════════════════════════════════════════
    //  Helpers
    // ══════════════════════════════════════════════════════════════════

    private static Func<string, string?> InMemoryFileReader(Dictionary<string, string> files)
    {
        return path =>
        {
            var normalized = StormImportResolver.CanonicalizePath(path);
            if(files.TryGetValue(normalized, out var contents))
            {
                return contents;
            }
            return null;
        };
    }

    private static GrammarIRLowering.LoweringResult LowerGrammar(string grammarSource)
    {
        var files = new Dictionary<string, string>
        {
            ["root/main.storm"] = grammarSource
        };

        var resolver = new StormImportResolver(InMemoryFileReader(files));
        var resolution = resolver.Resolve("root/main.storm");
        var grammar = StormGrammar.Build(resolution);
        return GrammarIRLowering.Lower(grammar);
    }

    // ══════════════════════════════════════════════════════════════════
    //  Property 3: Memo annotation propagation
    //  For any grammar source with rule declarations, IsMemo on each
    //  RuleIR equals true iff the corresponding StormRuleDecl had
    //  IsMemo = true.
    //  **Validates: Requirements 3.1, 3.2**
    // ══════════════════════════════════════════════════════════════════

    [Fact]
    public void P3_MemoPropagation_RuleIR_IsMemo_MatchesSource()
    {
        var samples = GenRuleDeclarations().Sample(200);
        foreach(var rules in samples)
        {
            if(rules.Count == 0)
            {
                continue;
            }

            var source = BuildGrammarSource(rules);
            var result = LowerGrammar(source);

            Assert.True(result.Succeeded,
                $"Grammar lowering failed for source:\n{source}\n" +
                $"Diagnostics: {string.Join(", ", result.Diagnostics.Select(d => d.Message))}");
            Assert.NotNull(result.Grammar);

            // Verify each rule's IsMemo matches the source declaration
            foreach(var (name, expectedIsMemo) in rules)
            {
                var ruleIR = result.Grammar!.Rules.FirstOrDefault(r => r.Name == name);
                Assert.True(ruleIR != null,
                    $"Rule '{name}' not found in lowered grammar IR.\nSource:\n{source}");
                Assert.True(ruleIR!.IsMemo == expectedIsMemo,
                    $"Rule '{name}': expected IsMemo={expectedIsMemo} but got IsMemo={ruleIR.IsMemo}.\nSource:\n{source}");
            }

            // Also verify count matches — no extra rules
            Assert.True(result.Grammar!.Rules.Count == rules.Count,
                $"Expected {rules.Count} rules but got {result.Grammar.Rules.Count}.\nSource:\n{source}");
        }
    }
}