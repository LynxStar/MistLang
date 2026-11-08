using System.Runtime.CompilerServices;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis;

/// <summary>
/// <c>target = target op value</c>, the assignment a <see cref="CompoundAssignmentExpression"/> stands for,
/// for a rule whose judgment of the node is exactly its judgment of that assignment. One instance per node, so
/// a rule that keys anything by the operation sees the same object each time it asks.
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