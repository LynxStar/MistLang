using System.Runtime.CompilerServices;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis;

/// <summary>
/// Cache one expansion per node so rules keyed by operation identity share it.
/// </summary>
internal static class CompoundAssignments
{
    private static readonly ConditionalWeakTable<CompoundAssignmentExpression, AssignmentExpression> LongForms = [];

    public static AssignmentExpression LongForm(CompoundAssignmentExpression compound)
    {
        return LongForms.GetValue(compound, written => new AssignmentExpression(
            written.Node,
            written.Target,
            new BinaryExpression(written.Node, written.Target, written.OperatorKind, written.Value, written.ResultType)
            {
                Operator = written.Operator,
                OperatorClosing = written.OperatorClosing,
                ParameterTypes = written.ParameterTypes,
            },
            written.Type));
    }
}