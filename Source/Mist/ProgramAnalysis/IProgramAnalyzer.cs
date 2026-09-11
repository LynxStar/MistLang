namespace Mist.ProgramAnalysis;

/// <summary>
/// One Stage 5 rule. It reads a finished model and reports; it changes nothing, because the model is
/// immutable after construction and every later rule reads the same one.
/// </summary>
internal interface IProgramAnalyzer
{
    string Id { get; }

    void Analyze(AnalysisContext context);
}