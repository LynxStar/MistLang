using Mist.Analysis.Contracts;
using Mist.Common.Diagnostics;
using Mist.Semantics.Contracts;

namespace Mist.Analysis.Builtin;

/// <summary>
/// Built-in analyzer that verifies Mist type rules against the semantic model.
/// Concrete type-checking rules are deferred to the Mist-language spec;
/// this establishes the architectural skeleton and contract.
/// </summary>
public sealed class TypeCheckerAnalyzer : IAnalyzer
{
    public string AnalyzerId => "TypeChecker";

    public void Analyze(ISemanticModel model, DiagnosticBag diagnostics)
    {
        // Walk all symbols and verify type rules.
        // Concrete type rules are deferred to the Mist-language spec.
        foreach (var symbol in model.AllSymbols)
        {
            // TODO: Implement type-checking rules once the Mist type system is defined.
            // For now, this is a no-op stub that establishes the analyzer contract.
        }
    }
}
