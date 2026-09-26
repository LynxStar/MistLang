using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Refinements;

internal sealed record RangeBound(long? Offset, LocalVariable? Symbol, bool FromEnd)
{
    public static RangeBound? Of(Expression expression)
    {
        var fromEnd = expression is UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd };
        var offset = fromEnd ? ((UnaryExpression)expression).Operand : expression;

        return Requirement.Constant(offset) is { } constant
            ? new RangeBound(constant, null, fromEnd)
            : offset is LocalReferenceExpression { Local.IsRef: false } local ? new RangeBound(null, local.Local, fromEnd) : null;
    }
}

internal sealed record StoredRange(RangeBound Start, RangeBound End)
{
    public static StoredRange? Of(RangeExpression range)
    {
        var start = range.Start is null ? new RangeBound(0, null, false) : RangeBound.Of(range.Start);
        var end = range.End is null ? new RangeBound(0, null, true) : RangeBound.Of(range.End);

        return start is not null && end is not null ? new StoredRange(start, end) : null;
    }

    public bool DependsOn(Func<object, bool> lost)
    {
        return (Start.Symbol is { } start && lost(start)) || (End.Symbol is { } end && lost(end));
    }
}