using System.Globalization;
using Mist.ProgramAnalysis;

namespace Mist.Driver;

internal static class StandardStageDetails
{
    public static IReadOnlyList<string> Model(int topLevelConstructs)
    {
        return [Invariant($"{topLevelConstructs} top-level constructs")];
    }

    /// <summary>
    /// Ends with what the stage spent outside every analyzer -- walking bodies, withholding what a clone
    /// repeats -- so the lines account for all of <paramref name="elapsed"/>.
    /// </summary>
    public static IReadOnlyList<string> Analysis(AnalysisTimings timings, TimeSpan elapsed)
    {
        var details = new List<string>
        {
            Invariant($"{timings.Analyzers.Count} analyzers over {timings.Scopes} scopes: {timings.ProgramScopes} program, {timings.CloneConversionScopes} clone conversions, {timings.ClonedTypeScopes} cloned types"),
        };

        foreach(var analyzer in timings.Analyzers)
        {
            details.Add(Invariant($"{analyzer.Id} ({analyzer.Name})  {analyzer.Elapsed.TotalSeconds:F2}s"));
        }

        var outside = elapsed - timings.InAnalyzers;
        details.Add(Invariant($"outside analyzers  {(outside > TimeSpan.Zero ? outside : TimeSpan.Zero).TotalSeconds:F2}s"));

        return details;
    }

    public static IReadOnlyList<string> Emission(int filesWritten, string? primaryArtifactPath)
    {
        var details = new List<string> { Invariant($"{filesWritten} .cs files written") };

        if(primaryArtifactPath is not null)
        {
            details.Add(primaryArtifactPath);
        }

        return details;
    }

    private static string Invariant(FormattableString text)
    {
        return text.ToString(CultureInfo.InvariantCulture);
    }
}