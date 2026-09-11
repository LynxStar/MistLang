using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;
using SyntaxNode = Storm.Runtime.SyntaxNode;

namespace Mist.ProgramAnalysis;

internal sealed class AnalysisContext(
    ProgramModelClass model, IReadOnlyList<object> nodes, DiagnosticBag diagnostics, string analyzerId)
{
    public ProgramModelClass Model { get; } = model;

    public IReadOnlyList<object> Nodes { get; } = nodes;

    /// <summary>
    /// Reports a finding ABOUT the given expressions, and stays silent when any of them already failed.
    /// Binding and analysis gather into one bag, so a rule runs over a model that may hold ErrorType --
    /// and a complaint about a value that did not bind is a second complaint about one mistake. Decided
    /// here, where every report passes, so that no rule has to remember it.
    /// </summary>
    public void Report(
        DiagnosticDescriptor descriptor,
        SyntaxNode at,
        IReadOnlyList<Expression> about,
        params ReadOnlySpan<object?> args)
    {
        if(about.Any(expression => expression.Type is ErrorType))
        {
            return;
        }

        diagnostics.ReportAtNode(descriptor, at, analyzerId, args);
    }
}