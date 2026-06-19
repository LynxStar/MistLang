// Feature: compiler-progress-output, Property 3: ConsoleProgressReporter stage formatting
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.CLI.Output;

namespace Mist.CLI.Tests.Output;

/// <summary>
/// Data record representing generated inputs for the stage formatting property test.
/// </summary>
public record StageFormattingInput(
    string StageId,
    string Title,
    IReadOnlyList<string> Details,
    TimeSpan Elapsed);

/// <summary>
/// Property-based tests for ConsoleProgressReporter stage formatting.
///
/// **Validates: Requirements 10.3, 10.4, 10.5, 10.6**
///
/// Property 3: For any stage number (0–6), title string, list of detail strings,
/// and elapsed TimeSpan, the ConsoleProgressReporter output SHALL:
/// - Start with a line matching "Stage {N} - {T}"
/// - Follow with each detail line prefixed by exactly four spaces
/// - End with a line matching "    Took {elapsed.TotalSeconds:F2}s" followed by a blank line
/// </summary>
public class ConsoleProgressReporterPropertyTests
{
    private static readonly char[] PrintableChars =
        Enumerable.Range(32, 95) // ASCII 32–126 (printable, no control chars)
            .Select(i => (char)i)
            .Where(c => c != '\n' && c != '\r') // extra safety: exclude newlines
            .ToArray();

    /// <summary>
    /// Generates random StageFormattingInput values with safe printable strings.
    /// </summary>
    public static Arbitrary<StageFormattingInput> ArbitraryStageFormattingInput()
    {
        var charGen = Gen.Elements(PrintableChars);

        var titleGen =
            from len in Gen.Choose(1, 20)
            from chars in Gen.ArrayOf(charGen, len)
            select new string(chars);

        var detailGen =
            from len in Gen.Choose(1, 30)
            from chars in Gen.ArrayOf(charGen, len)
            select new string(chars);

        var detailListGen =
            from count in Gen.Choose(0, 5)
            from details in Gen.ArrayOf(detailGen, count)
            select (IReadOnlyList<string>)details.ToList();

        var gen =
            from stageNumber in Gen.Choose(0, 6)
            from title in titleGen
            from details in detailListGen
            from totalSeconds in Gen.Choose(0, 10000) // 0–100.00 seconds in centiseconds
            let elapsed = TimeSpan.FromMilliseconds(totalSeconds * 10.0)
            select new StageFormattingInput(stageNumber.ToString(), title, details, elapsed);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 3: Stage formatting output matches expected structure.
    ///
    /// Calling ReportStageStart followed by ReportStageComplete produces output that:
    /// - Line 1: "Stage {N} - {Title}"
    /// - Lines 2..N+1: "    {detail}" for each detail
    /// - Next line: "    Took {elapsed.TotalSeconds:F2}s"
    /// - Final: blank line (empty WriteLine)
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterPropertyTests) })]
    public bool StageFormatting_MatchesExpectedOutput(StageFormattingInput input)
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportStageStart(input.StageId, input.Title);
        reporter.ReportStageComplete(input.StageId, input.Elapsed, input.Details);

        var output = writer.ToString();
        var expected = BuildExpectedOutput(input);

        return output == expected;
    }

    /// <summary>
    /// Property 3 (supplementary): Stage header line starts with "Stage {N} - ".
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterPropertyTests) })]
    public bool StageHeader_StartsWithStagePrefix(StageFormattingInput input)
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportStageStart(input.StageId, input.Title);

        var output = writer.ToString();
        var expectedHeader = $"Stage {input.StageId} - {input.Title}{Environment.NewLine}";

        return output == expectedHeader;
    }

    /// <summary>
    /// Property 3 (supplementary): Each detail line is indented with exactly four spaces.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterPropertyTests) })]
    public bool DetailLines_AreIndentedWithFourSpaces(StageFormattingInput input)
    {
        if (input.Details.Count == 0)
            return true; // vacuously true when no details

        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportStageComplete(input.StageId, input.Elapsed, input.Details);

        var lines = writer.ToString().Split(Environment.NewLine);

        // Check each detail line (first N lines correspond to details)
        for (var i = 0; i < input.Details.Count; i++)
        {
            var expectedLine = $"    {input.Details[i]}";
            if (lines[i] != expectedLine)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Property 3 (supplementary): Elapsed time line uses F2 format and is followed by blank line.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterPropertyTests) })]
    public bool ElapsedLine_UsesF2Format_FollowedByBlankLine(StageFormattingInput input)
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportStageComplete(input.StageId, input.Elapsed, input.Details);

        var lines = writer.ToString().Split(Environment.NewLine);

        // The elapsed line is after all detail lines
        var elapsedLineIndex = input.Details.Count;
        var expectedElapsedLine = $"    Took {input.Elapsed.TotalSeconds:F2}s";

        if (lines[elapsedLineIndex] != expectedElapsedLine)
            return false;

        // Next line should be empty (the blank separator from WriteLine())
        if (lines[elapsedLineIndex + 1].Length != 0)
            return false;

        return true;
    }

    private static string BuildExpectedOutput(StageFormattingInput input)
    {
        var sb = new System.Text.StringBuilder();

        // Header line from ReportStageStart
        sb.AppendLine($"Stage {input.StageId} - {input.Title}");

        // Detail lines from ReportStageComplete
        foreach (var detail in input.Details)
            sb.AppendLine($"    {detail}");

        // Elapsed time line
        sb.AppendLine($"    Took {input.Elapsed.TotalSeconds:F2}s");

        // Blank separator line (from WriteLine())
        sb.AppendLine();

        return sb.ToString();
    }
}

// Feature: compiler-progress-output, Property 4: ConsoleProgressReporter summary formatting

/// <summary>
/// Data record representing generated inputs for the summary formatting property test.
/// </summary>
public record SummaryFormattingInput(
    bool Succeeded,
    int ErrorCount,
    int WarningCount,
    TimeSpan TotalElapsed);

/// <summary>
/// Property-based tests for ConsoleProgressReporter summary formatting.
///
/// **Validates: Requirements 9.2, 9.3**
///
/// Property 4: For any boolean succeeded, non-negative integers errorCount and warningCount,
/// and TimeSpan totalElapsed, the ConsoleProgressReporter output SHALL match
/// "{status}  {E} error(s), {W} warning(s)  Total: {T.TT}s" where status is
/// "Build succeeded" or "Build failed".
/// </summary>
public class ConsoleProgressReporterSummaryPropertyTests
{
    /// <summary>
    /// Generates random SummaryFormattingInput values with non-negative counts and positive TimeSpans.
    /// </summary>
    public static Arbitrary<SummaryFormattingInput> ArbitrarySummaryFormattingInput()
    {
        var gen =
            from succeeded in Gen.Elements(true, false)
            from errorCount in Gen.Choose(0, 1000)
            from warningCount in Gen.Choose(0, 1000)
            from centiseconds in Gen.Choose(0, 10000) // 0–100.00 seconds in centiseconds
            let totalElapsed = TimeSpan.FromMilliseconds(centiseconds * 10.0)
            select new SummaryFormattingInput(succeeded, errorCount, warningCount, totalElapsed);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Property 4: Summary formatting output matches expected format.
    ///
    /// Calling ReportBuildSummary produces output matching:
    /// "{status}  {E} error(s), {W} warning(s)  Total: {X.XX}s\n"
    /// where status is "Build succeeded" or "Build failed".
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterSummaryPropertyTests) })]
    public bool SummaryFormatting_MatchesExpectedOutput(SummaryFormattingInput input)
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportBuildSummary(input.Succeeded, input.ErrorCount, input.WarningCount, input.TotalElapsed);

        var output = writer.ToString();
        var status = input.Succeeded ? "Build succeeded" : "Build failed";
        var expected = $"{status}  {input.ErrorCount} error(s), {input.WarningCount} warning(s)  Total: {input.TotalElapsed.TotalSeconds:F2}s{Environment.NewLine}";

        return output == expected;
    }

    /// <summary>
    /// Property 4 (supplementary): Summary status prefix is determined solely by the succeeded flag.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterSummaryPropertyTests) })]
    public bool SummaryStatus_DeterminedBySucceededFlag(SummaryFormattingInput input)
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportBuildSummary(input.Succeeded, input.ErrorCount, input.WarningCount, input.TotalElapsed);

        var output = writer.ToString();
        var expectedPrefix = input.Succeeded ? "Build succeeded" : "Build failed";

        return output.StartsWith(expectedPrefix);
    }

    /// <summary>
    /// Property 4 (supplementary): Summary elapsed time uses F2 format.
    /// </summary>
    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterSummaryPropertyTests) })]
    public bool SummaryElapsed_UsesF2Format(SummaryFormattingInput input)
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportBuildSummary(input.Succeeded, input.ErrorCount, input.WarningCount, input.TotalElapsed);

        var output = writer.ToString();
        var expectedSuffix = $"Total: {input.TotalElapsed.TotalSeconds:F2}s{Environment.NewLine}";

        return output.EndsWith(expectedSuffix);
    }
}
