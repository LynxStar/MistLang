using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts;

namespace Mist.ProgramModel;

/// <summary>
/// Builds an <see cref="IProgramModel"/> from parsed ASTs.
/// This is the v2 equivalent of <c>SemanticModelBuilder</c> — it constructs
/// the fully resolved program model without referencing the legacy semantic system.
/// </summary>
public sealed class ProgramModelBuilder
{
    /// <summary>
    /// Constructs an <see cref="IProgramModel"/> from the parsed ASTs and compilation metadata.
    /// </summary>
    /// <param name="asts">The syntax trees produced by Stage 3 (parsing).</param>
    /// <param name="diagnostics">Diagnostic bag to report construction errors.</param>
    /// <returns>The built program model, or null on fatal failure.</returns>
    // CA1822: Will use instance state once the builder is implemented.
#pragma warning disable CA1822
    public IProgramModel? Build(
        ImmutableArray<Storm.Runtime.SyntaxNode> asts,
        DiagnosticBag diagnostics)
#pragma warning restore CA1822
    {
        // TODO: Walk ASTs to build namespaces, types, members, conformances, extensions.
        _ = asts;

        diagnostics.Add(new Diagnostic(
            new DiagnosticId("MIST_EXPERIMENTAL"),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            "ProgramModelBuilder is not yet implemented.",
            Location: default,
            CompilationUnitPath: "<experimental>",
            AnalyzerName: null));

        //Create Scope per file

        //Pass 1
        //Loop files
        //Handle namespaces registration
        //Add namespaces to scope
        
        //Pass 2 

        return null;
    }
}