using System.Diagnostics;
using Mist.Common.Diagnostics;
using Mist.Lexing;
using Mist.Parsing;
using Mist.Tests.Helpers;
using Xunit.Abstractions;

namespace Mist.Tests.Integration;

[Collection(WallClockCollection.Name)]
[Trait("Tier", "3")]
public sealed class MemoizationCorrectnessAndPerformanceTests(ITestOutputHelper output)
{
    /// <summary>
    /// Memoization must be transparent to parsing results, so the whole corpus parses without errors.
    /// </summary>
    [Fact]
    public void AllFeaturesV2_ParsesWithoutErrors_WithExpandedMemoization()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "AllFeaturesV2");

        Assert.True(Directory.Exists(sampleDir), $"AllFeaturesV2 sample directory not found: {sampleDir}");

        var sourceFiles = CollectAllCorpusFiles(repoRoot);
        Assert.NotEmpty(sourceFiles);

        var lexer = new MistLexer();
        var parser = new MistParser();
        var parseErrors = new List<string>();

        foreach(var path in sourceFiles)
        {
            var source = new Mist.Common.Source.SourceText(path, File.ReadAllText(path));
            var lexBag = new DiagnosticBag();
            var tokens = lexer.Lex(source, lexBag);

            if(lexBag.ToImmutableArray().Any(d => d.Severity == DiagnosticSeverity.Error))
            {
                parseErrors.Add($"[LEX] {path}: {string.Join("; ", lexBag.ToImmutableArray().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => d.Message))}");
                continue;
            }

            var parseBag = new DiagnosticBag();
            var result = parser.Parse(tokens, parseBag);

            var errors = parseBag.ToImmutableArray()
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .ToList();

            if(errors.Count > 0)
            {
                parseErrors.Add($"[PARSE] {path}: {string.Join("; ", errors.Select(d => d.Message))}");
            }

            // Verify no parse stub warnings (parser is fully functional)
            var stubs = parseBag.ToImmutableArray()
                .Where(d => d.Number.Value == "MIST0121")
                .ToList();

            if(stubs.Count > 0)
            {
                parseErrors.Add($"[STUB] {path}: parser returned stub warning");
            }
        }

        Assert.True(
            parseErrors.Count == 0,
            $"Expanded memoization introduced {parseErrors.Count} error(s) across {sourceFiles.Count} files:\n" +
            string.Join("\n", parseErrors.Take(20)));
    }

    [Fact]
    public void AllFeaturesV2_ParsesWithinPerformanceTarget()
    {
        var repoRoot = FindRepoRoot();
        var sampleDir = Path.Combine(repoRoot, "samples", "AllFeaturesV2");

        Assert.True(Directory.Exists(sampleDir), $"AllFeaturesV2 sample directory not found: {sampleDir}");

        var sourceFiles = CollectAllCorpusFiles(repoRoot);
        Assert.NotEmpty(sourceFiles);

        var lexer = new MistLexer();
        var parser = new MistParser();

        // Load source texts into memory first (exclude I/O from timing)
        var sources = sourceFiles
            .Select(path => new Mist.Common.Source.SourceText(path, File.ReadAllText(path)))
            .ToList();

        // Warm-up pass: JIT the lex+parse code paths
        foreach(var source in sources)
        {
            var warmLexBag = new DiagnosticBag();
            var warmTokens = lexer.Lex(source, warmLexBag);
            var warmParseBag = new DiagnosticBag();
            _ = parser.Parse(warmTokens, warmParseBag);
        }

        var pauseBefore = GC.GetTotalPauseDuration();
        var collectionsBefore = GC.CollectionCount(2);
        var allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        var processCpuBefore = Process.GetCurrentProcess().TotalProcessorTime;
        var stopwatch = Stopwatch.StartNew();
        foreach(var source in sources)
        {
            var lexBag = new DiagnosticBag();
            var tokens = lexer.Lex(source, lexBag);
            var parseBag = new DiagnosticBag();
            _ = parser.Parse(tokens, parseBag);
        }

        stopwatch.Stop();

        var elapsed = stopwatch.Elapsed;
        var conditions =
            $"GC pause {(GC.GetTotalPauseDuration() - pauseBefore).TotalSeconds:F2}s, " +
            $"{GC.CollectionCount(2) - collectionsBefore} gen2 collections, " +
            $"{(GC.GetAllocatedBytesForCurrentThread() - allocatedBefore) / (1024 * 1024)} MB allocated by this thread, " +
            $"{(Process.GetCurrentProcess().TotalProcessorTime - processCpuBefore).TotalSeconds:F2}s CPU across the whole test process";
        output.WriteLine($"lex+parse {elapsed.TotalSeconds:F2}s wall; {conditions}");

        // Debug and non-tiered test runners can run well past the 2-second Release target, especially
        // under load, so this ceiling catches only catastrophic regressions.
        Assert.True(
            elapsed.TotalSeconds < 15.0,
            $"AllFeaturesV2 corpus lex+parse took {elapsed.TotalSeconds:F2}s, " +
            $"exceeding the 15-second regression ceiling ({sourceFiles.Count} files). " +
            $"The production target is 2 seconds (Release mode). During the measured pass: {conditions}.");
    }

    private static IReadOnlyList<string> CollectAllCorpusFiles(string repoRoot)
    {
        var samplesDir = Path.Combine(repoRoot, "samples");
        var files = new List<string>();

        foreach(var sampleProject in Directory.GetDirectories(samplesDir))
        {
            var dirName = Path.GetFileName(sampleProject);
            // Skip baseline and build artifact directories
            if(dirName.StartsWith("__") || dirName.Equals("bin", StringComparison.OrdinalIgnoreCase)
                || dirName.Equals("obj", StringComparison.OrdinalIgnoreCase)
                || dirName.Equals("out", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var mistFiles = Directory.GetFiles(sampleProject, "*.mist", SearchOption.TopDirectoryOnly);
            files.AddRange(mistFiles);
        }

        return files.OrderBy(f => f, StringComparer.Ordinal).ToList();
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

        dir = Directory.GetCurrentDirectory();
        while(dir is not null)
        {
            if(File.Exists(Path.Combine(dir, "Mist.slnx")))
            {
                return dir;
            }

            dir = Directory.GetParent(dir)?.FullName;
        }

        throw new InvalidOperationException("Could not find repository root (Mist.slnx)");
    }
}