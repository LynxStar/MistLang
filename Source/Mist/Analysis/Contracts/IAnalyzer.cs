using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;

namespace Mist.Analysis.Contracts;

/// <summary>
/// Contract for a pluggable analyzer that inspects a semantic model
/// and reports diagnostics without modifying the model.
/// </summary>
public interface IAnalyzer
{
    string AnalyzerId { get; }
    void Analyze(ISemanticModel model, DiagnosticBag diagnostics);
}