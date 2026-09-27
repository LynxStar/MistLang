using System.Text.RegularExpressions;

namespace Mist.Tests.Integration;

/// <summary>
/// Holds every spec's <c>#### Proof Code</c> block to the sample it is supposed to appear in, so a
/// requirements document cannot drift away from working code unnoticed.
/// <para>
/// It went unchecked long enough to matter: when this was first measured, 19 of 28 blocks had
/// drifted, one of them into code that could not compile at all, and the sample the
/// abstract-type-unification spec designates was referenced by no orchestrator and so run by
/// nothing. Every one of those was found by reading, which is the failure mode this replaces.
/// </para>
/// <para>
/// Reads files only. The companion invariant -- that a spec's <c>Expected output</c> equals what the
/// sample prints -- needs the sample compiled and run, so it is deliberately not asserted here; see
/// <see cref="SamplesCompileAndMatchBaselineTest"/> for what paying that cost looks like.
/// </para>
/// </summary>
public class SpecProofCodeMatchesSampleTest
{
    /// <summary>
    /// Blocks whose namespace names a sample other than the one their spec designates. Matching on
    /// the designated path alone would compare them against the wrong file.
    /// </summary>
    private static readonly Dictionary<(string Spec, int Block), string> Elsewhere = new()
    {
        [("attributes-and-test-command", 1)] = "samples/Testing/Tests.mist",
    };

    /// <summary>
    /// A block under a requirement other than the sample requirement, demonstrating that requirement
    /// in isolation. It belongs to no sample -- its namespace differs from the designated sample's --
    /// so there is nothing for it to be a substring of.
    /// </summary>
    private static readonly HashSet<(string Spec, int Block)> Illustrations =
    [
        ("abstract-type-unification", 0),
    ];

    private static readonly Regex Designated =
        new(@"sample project SHALL exist at `(samples/[^`]+)`", RegexOptions.Compiled);

    private static readonly Regex ProofCode =
        new(@"#### Proof Code[^\n]*\n+```mist\n(.*?)\n```", RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex ExpectedOutput = new(
        @"#### Proof Code[^\n]*\n+```mist\n.*?\n```\s*\n+\*\*Expected output:\*\*\n```\n(.*?)\n```",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Samples the classic orchestrator cannot reference, so their output is absent from its
    /// baseline. samples/DelegateFuncConversion carries a <c>Microsoft.AspNetCore.App</c> framework
    /// reference, and adding it to AllFeatures would give the whole orchestrator a web dependency.
    /// </summary>
    private static readonly HashSet<(string Spec, int Block)> OutsideTheOrchestrator =
    [
        ("delegate-func-conversion", 0),
    ];

    /// <summary>
    /// Blocks whose sample is V2-ONLY: referenced from AllFeaturesV2 alone, so their output is a slice
    /// of that baseline instead.
    /// </summary>
    private static readonly HashSet<(string Spec, int Block)> ExperimentalOnly =
    [
        ("purity", 0),
    ];

    [Fact]
    public void EveryProofCodeBlock_AppearsVerbatim_InItsDesignatedSample()
    {
        var repoRoot = FindRepoRoot();
        var specsDir = Path.Combine(repoRoot, ".kiro", "specs");
        Assert.True(Directory.Exists(specsDir), $"Specs directory not found: {specsDir}");

        var checked_ = 0;
        var failures = new List<string>();

        foreach(var requirements in Directory.GetFiles(specsDir, "requirements.md", SearchOption.AllDirectories).Order())
        {
            var spec = Path.GetFileName(Path.GetDirectoryName(requirements))!;
            var text = File.ReadAllText(requirements).Replace("\r\n", "\n");

            var designated = Designated.Match(text);
            if(!designated.Success)
            {
                continue;
            }

            var blocks = ProofCode.Matches(text);
            for(var i = 0; i < blocks.Count; i++)
            {
                if(Illustrations.Contains((spec, i)))
                {
                    continue;
                }

                var relative = Elsewhere.TryGetValue((spec, i), out var other) ? other : designated.Groups[1].Value;
                var sample = Path.Combine(repoRoot, relative.Replace('/', Path.DirectorySeparatorChar));

                if(!File.Exists(sample))
                {
                    continue;
                }

                checked_++;
                var body = blocks[i].Groups[1].Value.Replace("\r\n", "\n").Trim();

                if(!File.ReadAllText(sample).Replace("\r\n", "\n").Contains(body, StringComparison.Ordinal))
                {
                    failures.Add($"{spec} block {i} is not verbatim in {relative}");
                }
            }
        }

        Assert.True(checked_ > 20, $"Expected to check more than 20 proof-code blocks, checked {checked_}");
        Assert.True(
            failures.Count == 0,
            $"Proof code has drifted from its sample. The SAMPLE is authoritative -- update the spec "
                + $"to match it, per .kiro/steering/sample-proof-code.md:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));
    }

    /// <summary>
    /// The companion invariant, asserted against the classic baseline rather than by running
    /// anything. AllFeatures orchestrates nearly every sample, so its recorded stdout already says
    /// what they print, and <see cref="SamplesCompileAndMatchBaselineTest"/> is what keeps that file
    /// honest -- so this chains off an artifact something else already verifies instead of spawning
    /// 26 compilations of its own.
    /// <para>
    /// A SLICE rather than an equality, which is the same shape the proof-code assertion takes and is
    /// what makes it work for a spec documenting one section of a large sample. ownership-v2 states
    /// eight lines of a sample that prints ninety-eight, and needs no exemption for it.
    /// </para>
    /// </summary>
    [Fact]
    public void EveryExpectedOutputBlock_AppearsAsASlice_OfTheClassicBaseline()
    {
        var repoRoot = FindRepoRoot();
        var specsDir = Path.Combine(repoRoot, ".kiro", "specs");
        var baselineFile = Path.Combine(repoRoot, "samples", "__baselines__", "AllFeatures.out");
        Assert.True(File.Exists(baselineFile), $"Baseline not found: {baselineFile}");

        var baseline = File.ReadAllText(baselineFile).Replace("\r\n", "\n");
        var experimentalBaseline = File.ReadAllText(Path.Combine(repoRoot, "samples", "__baselines__", "AllFeaturesV2.out")).Replace("\r\n", "\n");
        var checkedBlocks = 0;
        var failures = new List<string>();

        foreach(var requirements in Directory.GetFiles(specsDir, "requirements.md", SearchOption.AllDirectories).Order())
        {
            var spec = Path.GetFileName(Path.GetDirectoryName(requirements))!;
            var text = File.ReadAllText(requirements).Replace("\r\n", "\n");

            if(!Designated.IsMatch(text))
            {
                continue;
            }

            var blocks = ExpectedOutput.Matches(text);
            for(var i = 0; i < blocks.Count; i++)
            {
                if(Illustrations.Contains((spec, i)) || OutsideTheOrchestrator.Contains((spec, i)))
                {
                    continue;
                }

                checkedBlocks++;
                var expected = blocks[i].Groups[1].Value.Replace("\r\n", "\n").Trim('\n');
                var experimental = ExperimentalOnly.Contains((spec, i));

                if(!(experimental ? experimentalBaseline : baseline).Contains(expected, StringComparison.Ordinal))
                {
                    failures.Add($"{spec} block {i} expected output is not a slice of {(experimental ? "AllFeaturesV2.out" : "AllFeatures.out")}");
                }
            }
        }

        Assert.True(checkedBlocks > 20, $"Expected to check more than 20 output blocks, checked {checkedBlocks}");
        Assert.True(
            failures.Count == 0,
            $"A spec claims output its sample does not produce. The SAMPLE is authoritative -- update "
                + $"the spec to match the baseline, per .kiro/steering/sample-proof-code.md:{Environment.NewLine}"
                + string.Join(Environment.NewLine, failures));
    }

    private static string FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test assembly location.");
    }
}