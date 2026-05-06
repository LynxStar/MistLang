using System.Collections.Immutable;
using System.Diagnostics;
using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;

namespace Mist.Analysis;

public sealed record AnalyzerTimingEntry(string AnalyzerId, TimeSpan Elapsed);

public sealed record AnalyzerRunResult(
    ImmutableArray<Diagnostic> Diagnostics,
    ImmutableArray<AnalyzerTimingEntry> Timings);

/// <summary>
/// Runs all registered analyzers against a semantic model, catching unhandled
/// exceptions per analyzer and emitting driver-level diagnostics.
/// </summary>
public sealed class AnalyzerRunner
{
    private ImmutableArray<IAnalyzer> _analyzers;

    public AnalyzerRunner(ImmutableArray<IAnalyzer> analyzers)
    {
        _analyzers = analyzers;
    }

    public AnalyzerRunResult RunAll(ISemanticModel model)
    {
        var bag = new DiagnosticBag();
        var timings = ImmutableArray.CreateBuilder<AnalyzerTimingEntry>();

        foreach(var analyzer in _analyzers)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                analyzer.Analyze(model, bag);
            }
            catch(Exception ex)
            {
                bag.Add(new Diagnostic(
                    new DiagnosticId("MIST_ANALYZER_CRASH"),
                    DiagnosticSeverity.Error,
                    PipelineStage.Analysis,
                    $"Analyzer '{analyzer.AnalyzerId}' threw an unhandled exception: {ex.Message}",
                    Location: null,
                    CompilationUnitPath: null,
                    AnalyzerName: analyzer.AnalyzerId));
            }
            sw.Stop();
            timings.Add(new AnalyzerTimingEntry(analyzer.AnalyzerId, sw.Elapsed));
        }

        return new AnalyzerRunResult(bag.ToImmutableArray(), timings.ToImmutable());
    }
}
