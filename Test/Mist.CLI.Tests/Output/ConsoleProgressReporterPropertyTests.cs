using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Mist.CLI.Output;

namespace Mist.CLI.Tests.Output;

public record StageFormattingInput(
    string StageId,
    string Title,
    IReadOnlyList<string> Details,
    TimeSpan Elapsed);

public class ConsoleProgressReporterPropertyTests
{
    private static readonly char[] PrintableChars =
        Enumerable.Range(32, 95) // printable ASCII
            .Select(i => (char)i)
            .Where(c => c is not '\n' and not '\r')
            .ToArray();

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
            from totalSeconds in Gen.Choose(0, 10000) // centiseconds, up to 100 s
            let elapsed = TimeSpan.FromMilliseconds(totalSeconds * 10.0)
            select new StageFormattingInput(stageNumber.ToString(), title, details, elapsed);

        return gen.ToArbitrary();
    }

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

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterPropertyTests) })]
    public bool DetailLines_AreIndentedWithFourSpaces(StageFormattingInput input)
    {
        if(input.Details.Count == 0)
        {
            return true;
        }

        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportStageComplete(input.StageId, input.Elapsed, input.Details);

        var lines = writer.ToString().Split(Environment.NewLine);

        for(var i = 0; i < input.Details.Count; i++)
        {
            var expectedLine = $"    {input.Details[i]}";
            if(lines[i] != expectedLine)
            {
                return false;
            }
        }

        return true;
    }

    [Property(MaxTest = 100, Arbitrary = new[] { typeof(ConsoleProgressReporterPropertyTests) })]
    public bool ElapsedLine_UsesF2Format_FollowedByBlankLine(StageFormattingInput input)
    {
        using var writer = new StringWriter();
        var reporter = new ConsoleProgressReporter(writer);

        reporter.ReportStageComplete(input.StageId, input.Elapsed, input.Details);

        var lines = writer.ToString().Split(Environment.NewLine);

        var elapsedLineIndex = input.Details.Count;
        var expectedElapsedLine = $"    Took {input.Elapsed.TotalSeconds:F2}s";

        if(lines[elapsedLineIndex] != expectedElapsedLine)
        {
            return false;
        }

        if(lines[elapsedLineIndex + 1].Length != 0)
        {
            return false;
        }

        return true;
    }

    private static string BuildExpectedOutput(StageFormattingInput input)
    {
        var sb = new System.Text.StringBuilder();

        _ = sb.AppendLine($"Stage {input.StageId} - {input.Title}");

        foreach(var detail in input.Details)
        {
            _ = sb.AppendLine($"    {detail}");
        }

        _ = sb.AppendLine($"    Took {input.Elapsed.TotalSeconds:F2}s");

        _ = sb.AppendLine();

        return sb.ToString();
    }
}

public record SummaryFormattingInput(
    bool Succeeded,
    int ErrorCount,
    int WarningCount,
    TimeSpan TotalElapsed);

public class ConsoleProgressReporterSummaryPropertyTests
{
    public static Arbitrary<SummaryFormattingInput> ArbitrarySummaryFormattingInput()
    {
        var gen =
            from succeeded in Gen.Elements(true, false)
            from errorCount in Gen.Choose(0, 1000)
            from warningCount in Gen.Choose(0, 1000)
            from centiseconds in Gen.Choose(0, 10000)
            let totalElapsed = TimeSpan.FromMilliseconds(centiseconds * 10.0)
            select new SummaryFormattingInput(succeeded, errorCount, warningCount, totalElapsed);

        return gen.ToArbitrary();
    }

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