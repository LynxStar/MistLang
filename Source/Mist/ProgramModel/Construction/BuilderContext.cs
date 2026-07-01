using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Construction;

/// <summary>
/// Shared compilation-wide state passed to all program model builders.
/// </summary>
internal sealed record BuilderContext(
    Scope GlobalScope,
    Model.ProgramModel ProgramModel,
    DiagnosticBag Diagnostics)
{
    /// <summary>
    /// Creates and reports an error diagnostic located at the given AST node.
    /// Source file and location are resolved from the node's origin.
    /// </summary>
    public void ReportError(string id, string message, Storm.Runtime.SyntaxNode node)
    {
        var (path, location, _) = DiagnosticHelper.ResolveLocation(node);

        Diagnostics.Add(new Diagnostic(
            new DiagnosticId(id),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            message,
            Location: location,
            CompilationUnitPath: path,
            AnalyzerName: null));
    }
}