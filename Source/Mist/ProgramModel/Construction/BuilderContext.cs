using Mist.Common.Diagnostics;
using Mist.Common.Source;
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
    /// </summary>
    public void ReportError(string id, string message, Storm.Runtime.SyntaxNode node, SourceText source)
    {
        var span = new SourceSpan(node.Span.Start, node.Span.Length);
        var location = source.ToLinePositionSpan(span);

        Diagnostics.Add(new Diagnostic(
            new DiagnosticId(id),
            DiagnosticSeverity.Error,
            PipelineStage.SemanticModelConstruction,
            message,
            Location: location,
            CompilationUnitPath: source.Path,
            AnalyzerName: null));
    }
}