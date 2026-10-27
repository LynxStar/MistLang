using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Positive expression refusals belong to one analysis scope. Dependencies follow semantic inputs and
/// local initializer provenance, never ErrorType or the diagnostic bag; pending fallbacks resolve from
/// their inputs first, even when a use is visited before its declaration.
/// </summary>
internal sealed class ExpressionFailures
{
    private readonly HashSet<Expression> refused = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<Expression> resolving = new(ReferenceEqualityComparer.Instance);

    public void Record(Expression expression)
    {
        _ = refused.Add(expression);
    }

    public bool Of(Expression expression)
    {
        if(refused.Contains(expression) || PriorFailure.Of(expression))
        {
            return true;
        }

        return FailureDependency.Inputs(expression).Any(Of);
    }

    public void Resolve(Expression expression, DiagnosticBag diagnostics)
    {
        if(!resolving.Add(expression))
        {
            return;
        }

        foreach(var input in FailureDependency.Inputs(expression))
        {
            Resolve(input, diagnostics);
        }

        if(expression is ErrorExpression { DeferredDiagnostic: { } pending } error)
        {
            if(!error.Children.Any(Of))
            {
                diagnostics.Replay(pending, error.Node);
            }

            Record(error);
        }
    }
}