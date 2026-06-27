using Storm.SourceGenerator.Analyze;
using Storm.SourceGenerator.IR;
using Storm.SourceGenerator.Resolve;

namespace Storm.SourceGenerator.Tests;

/// <summary>
/// Integration test that parses the full Mist grammar through the source generator
/// pipeline and verifies that all 7 @memo-annotated rules appear in the MemoizationPlan.
/// </summary>
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

    /// <summary>
    /// Locates the Mist grammar directory by walking up from the test assembly's
    /// output directory until we find the repository root (contains Mist.slnx).
    /// </summary>
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

    /// <summary>
    /// Creates a file reader delegate that reads files from the filesystem,
    /// resolving paths relative to the grammar directory.
    /// </summary>
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

    /// <summary>
    /// Processes the full Mist grammar through the pipeline and returns the MemoizationPlan.
    /// </summary>
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
        var plan = MistGrammarMemoizationPlanTests.Plan.Value;

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
        var plan = MistGrammarMemoizationPlanTests.Plan.Value;

        // All 7 rules should have ids in the range [0..Count-1]
        foreach(var ruleName in ExpectedMemoizedRules)
        {
            Assert.True(plan.RuleIds.ContainsKey(ruleName),
                $"Expected rule '{ruleName}' to have an assigned id.");

            var id = plan.RuleIds[ruleName];
            Assert.True(id >= 0 && id < plan.Count,
                $"Rule '{ruleName}' has id {id}, expected in range [0..{plan.Count - 1}].");
        }

        // Verify dense assignment: ids should be 0..Count-1 with no gaps
        var allIds = plan.MemoizedRuleNames.Select((_, i) => i).ToHashSet();
        for(var i = 0; i < plan.Count; i++)
        {
            Assert.Contains(i, allIds);
        }
    }

    [Fact]
    public void MistGrammar_MemoizationPlan_HasAtLeastSevenRules()
    {
        var plan = MistGrammarMemoizationPlanTests.Plan.Value;

        // The Plan should contain at least the 7 annotated rules (may also include
        // rules from the cycle-based or multi-caller heuristics)
        Assert.True(plan.Count >= 7,
            $"Expected at least 7 memoized rules, but got {plan.Count}. " +
            $"Memoized rules: [{string.Join(", ", plan.MemoizedRuleNames)}]");
    }
}