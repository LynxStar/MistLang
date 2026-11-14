using Mist.Common.Diagnostics;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Rules must not mutate the finished model: every later rule reads the same instance.
/// </summary>
internal interface IProgramAnalyzer
{
    string Id { get; }

    MistDiagnostic? OptionalRule => null;

    void Analyze(AnalysisContext context);
}