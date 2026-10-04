using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Model.Expressions;

/// <summary>
/// A failed expression retains its inputs for analysis. A deferred diagnostic is a fallback, reported
/// only if analysis finds no positive owner in those inputs; its presence is not a prior report.
/// </summary>
internal sealed class ErrorExpression(
    SyntaxNode node, IReadOnlyList<Expression>? children = null, Diagnostic? deferredDiagnostic = null) : Expression(node)
{
    public IReadOnlyList<Expression> Children { get; } = children ?? [];

    public Diagnostic? DeferredDiagnostic { get; } = deferredDiagnostic;

    public bool IsReported => DeferredDiagnostic is null;

    public override IMistType Type { get; } = ErrorType.Instance;
}