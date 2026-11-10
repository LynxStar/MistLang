using System.Collections.Immutable;
using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class ResultErrorFlow : IFlowLattice<ImmutableHashSet<LocalVariable>>
{
    public static void Judge(object root, AnalysisContext context)
    {
        Judge(context.GraphOf(root), context, ImmutableHashSet<LocalVariable>.Empty);
    }

    private static void Judge(FlowGraph graph, AnalysisContext context, ImmutableHashSet<LocalVariable> entry)
    {
        FlowSolution<ImmutableHashSet<LocalVariable>>.Solve(graph, new ResultErrorFlow(), entry).Replay((state, step) =>
        {
            if(step is { Kind: FlowStepKind.Evaluate, Node: ResultIdentityExpression identity } && IsError(identity.Operand, state))
            {
                context.Report(ProgramAnalysisDiagnostics.ResultErrorIsNotIdentity, identity.Node, [identity]);
            }

            if(graph.Nested.TryGetValue(step.Node, out var nested))
            {
                Judge(nested, context, state);
            }
        });
    }

    public ImmutableHashSet<LocalVariable> Join(ImmutableHashSet<LocalVariable> left, ImmutableHashSet<LocalVariable> right)
    {
        return left.Union(right);
    }

    public bool Same(ImmutableHashSet<LocalVariable> left, ImmutableHashSet<LocalVariable> right)
    {
        return left.SetEquals(right);
    }

    public ImmutableHashSet<LocalVariable> Transfer(ImmutableHashSet<LocalVariable> state, FlowStep step)
    {
        return step switch
        {
            { Kind: FlowStepKind.Bind, Variable: { IsKnownResultError: true } variable } => state.Add(variable),
            { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement { Local: var local, Initializer: { } value } } => Set(state, local, IsError(value, state)),
            { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { Target: LocalReferenceExpression { Local: var local }, Value: var value } } => Set(state, local, IsError(value, state)),
            { Kind: FlowStepKind.Evaluate, Node: CompoundAssignmentExpression { Target: LocalReferenceExpression { Local: var local } } } => state.Remove(local),
            _ => state,
        };
    }

    private static ImmutableHashSet<LocalVariable> Set(ImmutableHashSet<LocalVariable> state, LocalVariable local, bool error)
    {
        return error ? state.Add(local) : state.Remove(local);
    }

    private static bool IsError(Expression expression, ImmutableHashSet<LocalVariable> state)
    {
        return expression switch
        {
            LocalReferenceExpression reference => state.Contains(reference.Local),
            ResultIdentityExpression identity => IsError(identity.Operand, state),
            ImplicitConversionExpression conversion => IsError(conversion.Operand, state),
            CastExpression cast => IsError(cast.Operand, state),
            AsExpression converted => IsError(converted.Operand, state),
            _ => false,
        };
    }
}