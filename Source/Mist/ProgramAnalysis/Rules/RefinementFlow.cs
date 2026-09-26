using System.Collections.Immutable;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

internal sealed record Bound(object Holder, ComparisonKind Kind);

internal sealed record Knowledge(Admitted Values, ImmutableHashSet<Bound> Bounds, int Changes = 0, Admitted? Length = null, StoredRange? Slice = null, bool RefusedSlice = false, LiteralValue? Literal = null, RangeBound? IndexValue = null, RealSet? Real = null, int RealChanges = 0)
{
    public static readonly Knowledge Unknown = new(Admitted.Everything, ImmutableHashSet<Bound>.Empty);
}

/// <summary>
/// What the flow knows of each local as a refinement is proven: an interval set, widened to infinity the
/// third time a join moves it, so a counting loop settles while an inner loop revisited by an outer one
/// keeps the bound the outer condition gave, and a symbolic upper bound -- an index below `a.Length` --
/// remembered against the array itself, which is what indexing an array of unknown length needs. The
/// array may be a local or a field of this instance; the bound is forgotten when that holder is
/// reassigned and, for a field that is not readonly, at any call, object creation or await, since code
/// the flow cannot see may reassign it then. A local keeps nothing at all when
/// <see cref="UnseenWrites"/> names it.
/// </summary>
internal sealed class RefinementFlow(IReadOnlySet<object> unseenWrites, LiftedReturns lifted)
    : IFlowLattice<ImmutableDictionary<object, Knowledge>>
{
    public static readonly ImmutableDictionary<object, Knowledge> Nothing =
        ImmutableDictionary.Create<object, Knowledge>(ReferenceEqualityComparer.Instance);

    public ImmutableDictionary<object, Knowledge> Join(
        ImmutableDictionary<object, Knowledge> left, ImmutableDictionary<object, Knowledge> right)
    {
        var joined = Nothing;

        foreach(var (local, mine) in left)
        {
            if(!right.TryGetValue(local, out var theirs))
            {
                continue;
            }

            var values = mine.Values.Or(theirs.Values);
            var changes = mine.Changes;

            if(!values.Equals(mine.Values))
            {
                changes++;
                values = changes > 2 ? values.WidenedAgainst(mine.Values) : values;
            }

            var length = mine.Length is { } left2 && theirs.Length is { } right2 ? left2.Or(right2) : null;

            var slice = Equals(mine.Slice, theirs.Slice) ? mine.Slice : null;
            var literal = Equals(mine.Literal, theirs.Literal) ? mine.Literal : null;
            var index = Equals(mine.IndexValue, theirs.IndexValue) ? mine.IndexValue : null;
            var real = mine.Real is { } leftReal && theirs.Real is { } rightReal && leftReal.Kind == rightReal.Kind ? leftReal.Or(rightReal) : null;
            var realChanges = mine.RealChanges;

            if(real is not null && mine.Real is { } beforeReal && !real.Equals(beforeReal))
            {
                realChanges++;
                real = realChanges > 2 ? real.WidenedAgainst(beforeReal) : real;
            }

            joined = joined.Add(local, new Knowledge(values, mine.Bounds.Intersect(theirs.Bounds), changes, length, slice, mine.RefusedSlice && theirs.RefusedSlice, literal, index, real, realChanges));
        }

        return joined;
    }

    public bool Same(ImmutableDictionary<object, Knowledge> left, ImmutableDictionary<object, Knowledge> right)
    {
        return left.Count == right.Count
            && left.All(entry => right.TryGetValue(entry.Key, out var other)
                && entry.Value.Values.Equals(other.Values) && entry.Value.Bounds.SetEquals(other.Bounds)
                && Equals(entry.Value.Length, other.Length) && Equals(entry.Value.Slice, other.Slice)
                && entry.Value.RefusedSlice == other.RefusedSlice && Equals(entry.Value.Literal, other.Literal)
                && Equals(entry.Value.IndexValue, other.IndexValue) && Equals(entry.Value.Real, other.Real));
    }

    public ImmutableDictionary<object, Knowledge> Transfer(ImmutableDictionary<object, Knowledge> state, FlowStep step)
    {
        var next = Step(state, step);

        return unseenWrites.Count == 0 ? next : Forget(next, IsWrittenUnseen, andTheHolder: true);
    }

    private bool IsWrittenUnseen(object holder)
    {
        return unseenWrites.Contains(holder);
    }

    private ImmutableDictionary<object, Knowledge> Step(ImmutableDictionary<object, Knowledge> state, FlowStep step)
    {
        switch(step)
        {
            case { Kind: FlowStepKind.Assume, Node: Expression condition }:
                return Learn(state, condition, step.Holds);

            case { Kind: FlowStepKind.Introduce or FlowStepKind.Bind, Variable: { } introduced }:
                return Forget(state, holder => ReferenceEquals(holder, introduced), andTheHolder: true);

            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration }:
                state = Forget(state, holder => ReferenceEquals(holder, declaration.Local), andTheHolder: true);
                return Given(state, declaration.Local, declaration.Initializer);

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { IsCoalescing: false, Target: LocalReferenceExpression target } assignment }:
                state = Forget(state, holder => ReferenceEquals(holder, target.Local));

                if(assignment.Value is BinaryExpression { Left: LocalReferenceExpression { Local: var same }, OperatorKind: BinaryOperatorKind.Add or BinaryOperatorKind.Subtract } step2
                    && ReferenceEquals(same, target.Local) && Requirement.Constant(step2.Right) is { } by && state.TryGetValue(target.Local, out var before))
                {
                    return state.SetItem(target.Local, new Knowledge(before.Values.Shifted(step2.OperatorKind == BinaryOperatorKind.Add ? by : -by), ImmutableHashSet<Bound>.Empty));
                }

                return Given(state, target.Local, assignment.Value);

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { Target: var target } assignment } when Assigned(target) is { } field:
                {
                    var length = Length(state, assignment.Value, lifted);
                    var refused = Refused(state, assignment.Value, lifted);
                    var index = RangeBound.IsIndex(field.Type) ? RangeKnowledge.Index(state, assignment.Value, lifted) : null;
                    state = Forget(state, holder => ReferenceEquals(holder, field));

                    return Holder(target) is Field owned && (length is not null || refused || index is not null)
                        ? state.SetItem(owned, Knowledge.Unknown with { Length = length, RefusedSlice = refused, IndexValue = index })
                        : state;
                }

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { Target: ThisExpression } }:
                return Forget(state, holder => holder is Field);

            case { Kind: FlowStepKind.Evaluate, Node: InvocationExpression or NewExpression }:
                return Forget(state, IsMutableField);

            case { Kind: FlowStepKind.Evaluate, Node: Expression accessed } when RunsAccessor(accessed):
                return Forget(state, IsMutableField);

            case { Kind: FlowStepKind.Evaluate, Node: AssignmentExpression { Target: var written } } when RunsAccessor(written):
                return Forget(state, IsMutableField);

            default:
                return state;
        }
    }

    private static ImmutableDictionary<object, Knowledge> Learn(
        ImmutableDictionary<object, Knowledge> state, Expression condition, bool holds)
    {
        if(condition is BinaryExpression { OperatorKind: BinaryOperatorKind.NotEqual })
        {
            holds = !holds;
        }

        return condition switch
        {
            BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true }, Left: MemberAccessExpression { MemberName: "Length", Receiver: var sizedOne }, Right: var limit2 } sized
                            when RangeKnowledge.IsLength((MemberAccessExpression)sized.Left) && Holder(sizedOne) is { } oneHolder && Comparison(sized.OperatorKind) is { } sizedKind && Requirement.Constant(limit2) is { } sizedBound => Sized(state, oneHolder, Requirement.Interval(sizedKind, sizedBound), holds),
            BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true }, Left: var limit3, Right: MemberAccessExpression { MemberName: "Length", Receiver: var sizedTwo } } sizedFlip
                            when RangeKnowledge.IsLength((MemberAccessExpression)sizedFlip.Right) && Holder(sizedTwo) is { } twoHolder && Comparison(sizedFlip.OperatorKind) is { } flipKind && Requirement.Constant(limit3) is { } flipBound => Sized(state, twoHolder, Requirement.Interval(Mirrored(flipKind), flipBound), holds),
            BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true }, Left: LocalReferenceExpression subject, Right: var bound } comparison when Comparison(comparison.OperatorKind) is { } kind => Compared(state, subject.Local, bound, kind, holds),
            BinaryExpression { Operator: null or { Modifiers.IsIntrinsic: true }, Left: var limit, Right: LocalReferenceExpression mirrored } flipped when Comparison(flipped.OperatorKind) is { } mirroredKind => Compared(state, mirrored.Local, limit, Mirrored(mirroredKind), holds),
            IsPatternExpression { Scrutinee: LocalReferenceExpression realSubject, Pattern: var realPattern }
                            when RealRequirement.KindOf(realSubject.Type) is { } realKind && RealRequirement.Of(realPattern, realKind) is { } realMatched
                            => RealKnowledge.Narrow(state, realSubject.Local, holds ? realMatched : realMatched.Not()),
            IsPatternExpression { Scrutinee: LocalReferenceExpression listed, Pattern: ListPattern list } when holds => Sliced(state, listed, list),
            IsPatternExpression { Scrutinee: LocalReferenceExpression subject, Pattern: var pattern }
                            when Requirement.Of(pattern) is { Bounds.Length: 0 } matched => Narrow(state, subject.Local, holds ? matched.Values : matched.Values.Not()),
            _ => state,
        };
    }

    private static ImmutableDictionary<object, Knowledge> Compared(
        ImmutableDictionary<object, Knowledge> state, LocalVariable subject, Expression bound, ComparisonKind kind, bool holds)
    {
        if(RealRequirement.KindOf(subject.Type) is not null)
        {
            return RealKnowledge.Compared(state, subject, bound, kind, holds);
        }

        if(Requirement.Constant(bound) is { } constant)
        {
            return Narrow(state, subject, holds ? Requirement.Interval(kind, constant) : Requirement.Interval(kind, constant).Not());
        }

        var upper = holds ? kind : Flipped(kind);

        if(bound is not MemberAccessExpression { Receiver: var measured } access || !RangeKnowledge.IsLength(access)
            || upper is not (ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual))
        {
            return state;
        }

        if(Lengths.Exact(Lengths.Typed(measured.Type)) is { } length)
        {
            return Narrow(state, subject, Requirement.Interval(upper, length));
        }

        if(Holder(measured) is not { } holder)
        {
            return state;
        }

        var known = state.TryGetValue(subject, out var existing) ? existing : Knowledge.Unknown;

        return state.SetItem(subject, known with { Bounds = known.Bounds.Add(new Bound(holder, upper)) });
    }

    private static ImmutableDictionary<object, Knowledge> Narrow(
        ImmutableDictionary<object, Knowledge> state, LocalVariable local, Admitted values)
    {
        var known = state.TryGetValue(local, out var existing) ? existing : Knowledge.Unknown;
        return state.SetItem(local, known with { Values = known.Values.And(values) });
    }

    private ImmutableDictionary<object, Knowledge> Given(
        ImmutableDictionary<object, Knowledge> state, LocalVariable local, Expression? value)
    {
        if(value is null)
        {
            return state.Remove(local);
        }

        if(RangeBound.IsIndex(local.Type))
        {
            var index = RangeKnowledge.Index(state, value, lifted);
            return index is null ? state.Remove(local) : state.SetItem(local, Knowledge.Unknown with { IndexValue = index });
        }

        if(RealRequirement.KindOf(local.Type) is not null)
        {
            var real = RealKnowledge.Assigned(state, value, local.Type);
            var exact = LiteralValue.Of(value, held => state.TryGetValue(held, out var known) ? known.Literal : null)?.ConvertedTo(local.Type);
            return real is null ? state.Remove(local) : state.SetItem(local, Knowledge.Unknown with { Real = real, Literal = exact });
        }

        if(Requirement.Constant(value) is { } constant)
        {
            return state.SetItem(local, new Knowledge(Admitted.Exactly(constant), ImmutableHashSet<Bound>.Empty)
            {
                Literal = LiteralValue.Of(value)?.ConvertedTo(local.Type),
            });
        }

        if(value is LocalReferenceExpression source && state.TryGetValue(source.Local, out var copied))
        {
            return state.SetItem(local, copied with { Literal = copied.Literal?.ConvertedTo(local.Type) });
        }

        if(value is MemberAccessExpression { Receiver: { } measured } access && RangeKnowledge.IsLength(access))
        {
            return state.SetItem(local, new Knowledge(Length(state, measured, lifted) ?? Admitted.AtLeast(0), ImmutableHashSet<Bound>.Empty));
        }

        if(lifted.Produced(value) is { } produced)
        {
            return state.SetItem(local, new Knowledge(produced, ImmutableHashSet<Bound>.Empty));
        }

        if(value is RangeExpression written)
        {
            return state.SetItem(local, Knowledge.Unknown with { Slice = Range(state, written, lifted) });
        }

        if(value is IndexExpression slice && Lengths.IsRangeSlice(slice) && Range(state, slice.Index, lifted) is { } range)
        {
            var sourceLength = Length(state, slice.Receiver, lifted);
            var refused = Refused(state, slice.Receiver, lifted) || Lengths.SafetyOf(range, sourceLength) == RangeSafety.Violated;

            return state.SetItem(local, Knowledge.Unknown with { Length = Lengths.OfSlice(range, sourceLength), RefusedSlice = refused });
        }

        if(Refused(state, value, lifted))
        {
            return state.SetItem(local, Knowledge.Unknown with { RefusedSlice = true });
        }

        var literal = LiteralValue.Of(value, held => state.TryGetValue(held, out var known) ? known.Literal : null)?.ConvertedTo(local.Type);
        var length = Length(state, value, lifted);
        return literal is not null || length is not null
            ? state.SetItem(local, Knowledge.Unknown with { Length = length, Literal = literal })
            : state.Remove(local)
            ;
    }

    private static ImmutableDictionary<object, Knowledge> Forget(
        ImmutableDictionary<object, Knowledge> state, Func<object, bool> lost, bool andTheHolder = false)
    {
        foreach(var (other, known) in state)
        {
            if((other is Field || andTheHolder) && lost(other))
            {
                state = state.Remove(other);
            }
            else
            {
                var bounds = known.Bounds.Where(bound => !lost(bound.Holder)).ToImmutableHashSet();
                if(bounds.Count != known.Bounds.Count)
                {
                    state = state.SetItem(other, known with { Bounds = bounds });
                }
            }
        }

        return state;
    }

    private static ImmutableDictionary<object, Knowledge> Sliced(
        ImmutableDictionary<object, Knowledge> state, LocalReferenceExpression scrutinee, ListPattern list)
    {
        var fixedCount = list.Elements.Count(element => !element.IsSlice);
        var sliced = list.Elements.Any(element => element.IsSlice);
        var length = (Held(state, scrutinee) ?? Lengths.Typed(scrutinee.Type) ?? Admitted.Everything)
            .And(sliced ? Admitted.AtLeast(fixedCount) : Admitted.Exactly(fixedCount));

        state = Sized(state, scrutinee.Local, length, holds: true);

        if(list.Elements.FirstOrDefault(element => element.IsSlice)?.Subpattern?.Binding is { } middle)
        {
            state = state.SetItem(middle, Knowledge.Unknown with { Length = length.Shifted(-fixedCount) });
        }

        return state;
    }

    /// <summary>
    /// A length learned from a condition, and never negative -- which every caller here already assumes of
    /// the member it read. Without that, the negation of an EQUALITY carries nothing usable: `Length == 0`
    /// failing leaves the values below zero beside those above it, so `Length >= 1` does not follow, where
    /// the negation of an inequality needs no such help.
    /// </summary>
    private static ImmutableDictionary<object, Knowledge> Sized(
        ImmutableDictionary<object, Knowledge> state, object measured, Admitted admitted, bool holds)
    {
        var known = state.TryGetValue(measured, out var existing) ? existing : Knowledge.Unknown;
        var learned = (holds ? admitted : admitted.Not()).And(Admitted.AtLeast(0));

        return state.SetItem(measured, known with { Length = known.Length is { } already ? already.And(learned) : learned });
    }

    public static Admitted? Held(ImmutableDictionary<object, Knowledge> state, Expression? receiver)
    {
        return Holder(receiver) is { } holder && state.TryGetValue(holder, out var known)
            ? known.Length
            : null
            ;
    }

    public static StoredRange? Range(ImmutableDictionary<object, Knowledge> state, Expression index, LiftedReturns? lifted = null)
    {
        return index switch
        {
            RangeExpression range => RangeKnowledge.Range(state, range, lifted),
            LocalReferenceExpression local when state.TryGetValue(local.Local, out var known) => known.Slice,
            _ => null,
        };
    }

    public static Admitted? Length(ImmutableDictionary<object, Knowledge> state, Expression? expression, LiftedReturns? lifted = null)
    {
        return Lengths.Of(expression, held => Held(state, held), range => Range(state, range, lifted));
    }

    public static bool Refused(ImmutableDictionary<object, Knowledge> state, Expression expression, LiftedReturns? lifted = null)
    {
        return expression switch
        {
            ImplicitConversionExpression conversion => Refused(state, conversion.Operand, lifted),
            CloneExpression clone => Refused(state, clone.Source, lifted),
            LocalReferenceExpression local => state.TryGetValue(local.Local, out var known) && known.RefusedSlice,
            FieldReferenceExpression or MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult }
                => Holder(expression) is { } field && state.TryGetValue(field, out var held) && held.RefusedSlice,
            IndexExpression slice when Lengths.IsRangeSlice(slice) => Refused(state, slice.Receiver, lifted)
                || (Range(state, slice.Index, lifted) is { } range && Lengths.SafetyOf(range, Length(state, slice.Receiver, lifted)) == RangeSafety.Violated),
            _ => false,
        };
    }

    private static bool IsMutableField(object holder)
    {
        return holder is Field { Modifiers.IsReadonly: false };
    }

    private static bool RunsAccessor(Expression accessed)
    {
        return accessed switch
        {
            MemberAccessExpression { Member: PropertyResult { Property: var property } } => HasBody(property.Getter) || HasBody(property.Setter),
            PropertyReferenceExpression { Property: var property } => HasBody(property.Getter) || HasBody(property.Setter),
            IndexExpression { Indexer: { } indexer } => HasBody(indexer.Getter) || HasBody(indexer.Setter),
            _ => false,
        };
    }

    private static bool HasBody(PropertyAccessor? accessor)
    {
        return accessor is { IsAuto: false } && (accessor.Body is not null || accessor.ExpressionBody is not null);
    }

    public static object? Holder(Expression? array)
    {
        return array switch
        {
            LocalReferenceExpression local => local.Local,
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult { Field: var field } } => field,
            _ => null,
        };
    }

    private static Field? Assigned(Expression target)
    {
        return target switch
        {
            FieldReferenceExpression field => field.Field,
            MemberAccessExpression { Member: FieldResult { Field: var field } } => field,
            _ => null,
        };
    }

    private static ComparisonKind? Comparison(BinaryOperatorKind kind)
    {
        return kind switch
        {
            BinaryOperatorKind.Equal or BinaryOperatorKind.NotEqual => ComparisonKind.Equal,
            BinaryOperatorKind.LessThan => ComparisonKind.LessThan,
            BinaryOperatorKind.GreaterThan => ComparisonKind.GreaterThan,
            BinaryOperatorKind.LessThanOrEqual => ComparisonKind.LessThanOrEqual,
            BinaryOperatorKind.GreaterThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            _ => null,
        };
    }

    private static ComparisonKind Mirrored(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => ComparisonKind.GreaterThan,
            ComparisonKind.GreaterThan => ComparisonKind.LessThan,
            ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThanOrEqual,
            ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThanOrEqual,
            _ => kind,
        };
    }

    private static ComparisonKind Flipped(ComparisonKind kind)
    {
        return kind switch
        {
            ComparisonKind.LessThan => ComparisonKind.GreaterThanOrEqual,
            ComparisonKind.GreaterThan => ComparisonKind.LessThanOrEqual,
            ComparisonKind.LessThanOrEqual => ComparisonKind.GreaterThan,
            ComparisonKind.GreaterThanOrEqual => ComparisonKind.LessThan,
            _ => kind,
        };
    }
}