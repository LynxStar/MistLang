using System.Collections.Immutable;
using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;

namespace Mist.Analysis;

/// <summary>
/// Runs all registered analyzers against a semantic model, catching unhandled
/// exceptions per analyzer and emitting driver-level diagnostics.
/// </summary>
public sealed class AnalyzerRunner
{
    private readonly ImmutableArray<IAnalyzer> _analyzers;

    public AnalyzerRunner(ImmutableArray<IAnalyzer> analyzers)
    {
        _analyzers = analyzers;
    }

    public ImmutableArray<Diagnostic> RunAll(ISemanticModel model)
    {
        var bag = new DiagnosticBag();

        foreach (var analyzer in _analyzers)
        {
            try
            {
                analyzer.Analyze(model, bag);
            }
            catch (Exception ex)
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
        }

        return bag.ToImmutableArray();
    }
}
