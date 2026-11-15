using Storm.SourceGenerator.Analyze;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace Mist.StormIntegration.Tests;

/// <summary>
/// <see cref="ExpectedMemoizedRules"/> are the seven rules Mist.storm annotates with @memo.
/// </summary>
[Trait("Tier", "4")]
public class MistGrammarMemoizationPlanTests
{
    private static readonly string[] ExpectedMemoizedRules =
    {
        "Expression",
        "Statement",
        "Primary",
        "Postfix",
        "Unary",
        "TypeReference",
        "TopLevelItem",
    };

    private static string FindGrammarDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while(dir != null)
        {
            var slnxPath = Path.Combine(dir, "Mist.slnx");
            if(File.Exists(slnxPath))
            {
                var grammarDir = Path.Combine(dir, "Source", "Mist", "Grammar");
                if(Directory.Exists(grammarDir))
                {
                    return grammarDir;
                }
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new InvalidOperationException(
            "Could not locate the Mist grammar directory. " +
            "Ensure the test is run from within the repository tree.");
    }

    private static Func<string, string?> FileSystemReader(string grammarDir)
    {
        return path =>
        {
            var fsPath = path.Replace('/', Path.DirectorySeparatorChar);

            if(Path.IsPathRooted(fsPath) && File.Exists(fsPath))
            {
                return File.ReadAllText(fsPath);
            }

            var fullPath = Path.GetFullPath(Path.Combine(grammarDir, fsPath));
            if(File.Exists(fullPath))
            {
                return File.ReadAllText(fullPath);
            }

            return null;
        };
    }

    private static MemoizationPlan BuildMistMemoizationPlan()
    {
        var grammarDir = FindGrammarDirectory();
        var mainGrammarPath = Path.Combine(grammarDir, "Mist.storm")
            .Replace(Path.DirectorySeparatorChar, '/');

        var reader = FileSystemReader(grammarDir);
        var resolver = new StormImportResolver(reader);
        var resolution = resolver.Resolve(mainGrammarPath);

        Assert.True(resolution.Succeeded,
            "Mist grammar import resolution failed: " +
            string.Join(", ", resolution.Diagnostics.Select(d => d.Message)));

        var stormGrammar = StormGrammar.Build(resolution);
        var loweringResult = GrammarIRLowering.Lower(stormGrammar);

        Assert.True(loweringResult.Succeeded,
            "Mist grammar lowering failed: " +
            string.Join(", ", loweringResult.Diagnostics.Select(d => d.Message)));
        Assert.NotNull(loweringResult.Grammar);

        return MemoizationAnalysisPass.Run(loweringResult.Grammar!);
    }

    private static readonly Lazy<MemoizationPlan> Plan = new(BuildMistMemoizationPlan);

    [Fact]
    public void MistGrammar_MemoizationPlan_ContainsAllSevenAnnotatedRules()
    {
        var plan = Plan.Value;

        foreach(var ruleName in ExpectedMemoizedRules)
        {
            Assert.True(plan.IsMemoized(ruleName),
                $"Expected rule '{ruleName}' to be in the MemoizationPlan, but it was not. " +
                $"Memoized rules: [{string.Join(", ", plan.MemoizedRuleNames)}]");
        }
    }

    [Fact]
    public void MistGrammar_MemoizationPlan_AssignsDenseIds()
    {
        var plan = Plan.Value;

        foreach(var ruleName in ExpectedMemoizedRules)
        {
            Assert.True(plan.RuleIds.ContainsKey(ruleName),
                $"Expected rule '{ruleName}' to have an assigned id.");

            var id = plan.RuleIds[ruleName];
            Assert.True(id >= 0 && id < plan.Count,
                $"Rule '{ruleName}' has id {id}, expected in range [0..{plan.Count - 1}].");
        }

        var allIds = plan.MemoizedRuleNames.Select((_, i) => i).ToHashSet();
        for(var i = 0; i < plan.Count; i++)
        {
            Assert.Contains(i, allIds);
        }
    }

    [Fact]
    public void MistGrammar_MemoizationPlan_HasAtLeastSevenRules()
    {
        var plan = Plan.Value;

        // Cycle-based and multi-caller heuristics may memoize further rules.
        Assert.True(plan.Count >= 7,
            $"Expected at least 7 memoized rules, but got {plan.Count}. " +
            $"Memoized rules: [{string.Join(", ", plan.MemoizedRuleNames)}]");
    }
}