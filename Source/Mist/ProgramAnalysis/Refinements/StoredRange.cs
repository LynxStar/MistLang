using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Refinements;

// Captured offsets never read their symbols again; identity distinguishes non-exact bound descriptors at joins.
internal sealed record RangeBound(Admitted Values, LocalVariable? Symbol, bool FromEnd)
{
    public static RangeBound? Of(Expression expression, Func<Expression, Admitted?>? captured = null)
    {
        var fromEnd = expression is UnaryExpression { OperatorKind: UnaryOperatorKind.FromEnd };
        var offset = fromEnd ? ((UnaryExpression)expression).Operand : expression;

        if(Requirement.Constant(offset) is { } constant)
        {
            return new RangeBound(Admitted.Exactly(constant), null, fromEnd);
        }

        if(offset is not LocalReferenceExpression { Local.IsRef: false } local || Requirement.Unrefined(offset.Type).FullyQualifiedName == "Standard.Index")
        {
            return null;
        }

        var values = captured?.Invoke(offset) ?? Requirement.ValuesOf(offset.Type) ?? Admitted.Everything;

        return new RangeBound(values, Lengths.Exact(values) is null ? local.Local : null, fromEnd);
    }
}

internal sealed record StoredRange(RangeBound Start, RangeBound End)
{
    public static StoredRange? Of(RangeExpression range, Func<Expression, Admitted?>? captured = null)
    {
        var start = range.Start is null ? new RangeBound(Admitted.Exactly(0), null, false) : RangeBound.Of(range.Start, captured);
        var end = range.End is null ? new RangeBound(Admitted.Exactly(0), null, true) : RangeBound.Of(range.End, captured);

        return start is not null && end is not null ? new StoredRange(start, end) : null;
    }
}