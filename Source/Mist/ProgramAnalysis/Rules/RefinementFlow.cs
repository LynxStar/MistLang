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

internal sealed record Knowledge(Admitted Values, ImmutableHashSet<Bound> Bounds, int Changes = 0, Admitted? Length = null)
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
/// the flow cannot see may reassign it then.
/// </summary>
internal sealed class RefinementFlow : IFlowLattice<ImmutableDictionary<object, Knowledge>>
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

            joined = joined.Add(local, new Knowledge(values, mine.Bounds.Intersect(theirs.Bounds), changes, length));
        }

        return joined;
    }

    public bool Same(ImmutableDictionary<object, Knowledge> left, ImmutableDictionary<object, Knowledge> right)
    {
        return left.Count == right.Count
            && left.All(entry => right.TryGetValue(entry.Key, out var other)
                && entry.Value.Values.Equals(other.Values) && entry.Value.Bounds.SetEquals(other.Bounds)
                && Equals(entry.Value.Length, other.Length));
    }

    public ImmutableDictionary<object, Knowledge> Transfer(ImmutableDictionary<object, Knowledge> state, FlowStep step)
    {
        switch(step)
        {
            case { Kind: FlowStepKind.Assume, Node: Expression condition }:
                return Learn(state, condition, step.Holds);

            case { Kind: FlowStepKind.Declare, Node: LocalDeclarationStatement declaration }:
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
                    var length = Lengths.Of(assignment.Value, held => Held(state, held));
                    state = Forget(state, holder => ReferenceEquals(holder, field));

                    return Holder(target) is Field owned && length is not null ? state.SetItem(owned, Knowledge.Unknown with { Length = length }) : state;
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
        switch(condition)
        {
            case BinaryExpression { Left: MemberAccessExpression { MemberName: "Length", Receiver: var sizedOne }, Right: var limit2 } sized
                when Holder(sizedOne) is { } oneHolder && Comparison(sized.OperatorKind) is { } sizedKind && Requirement.Constant(limit2) is { } sizedBound:
                return Sized(state, oneHolder, Requirement.Interval(sizedKind, sizedBound), holds);

            case BinaryExpression { Left: var limit3, Right: MemberAccessExpression { MemberName: "Length", Receiver: var sizedTwo } } sizedFlip
                when Holder(sizedTwo) is { } twoHolder && Comparison(sizedFlip.OperatorKind) is { } flipKind && Requirement.Constant(limit3) is { } flipBound:
                return Sized(state, twoHolder, Requirement.Interval(Mirrored(flipKind), flipBound), holds);

            case BinaryExpression { Left: LocalReferenceExpression subject, Right: var bound } comparison when Comparison(comparison.OperatorKind) is { } kind:
                if(Requirement.Constant(bound) is { } constant)
                {
                    return Narrow(state, subject.Local, holds ? Requirement.Interval(kind, constant) : Requirement.Interval(kind, constant).Not());
                }

                var upper = holds ? kind : Flipped(kind);

                if(bound is MemberAccessExpression { MemberName: "Length", Receiver: var measured }
                    && upper is ComparisonKind.LessThan or ComparisonKind.LessThanOrEqual)
                {
                    if(Lengths.Exact(Lengths.Typed(measured.Type)) is { } length)
                    {
                        return Narrow(state, subject.Local, Requirement.Interval(upper, length));
                    }

                    if(Holder(measured) is { } holder)
                    {
                        var known = state.TryGetValue(subject.Local, out var existing) ? existing : Knowledge.Unknown;
                        return state.SetItem(subject.Local, known with { Bounds = known.Bounds.Add(new Bound(holder, upper)) });
                    }
                }

                return state;

            case BinaryExpression { Left: var limit, Right: LocalReferenceExpression mirrored } flipped
                when Comparison(flipped.OperatorKind) is { } mirroredKind && Requirement.Constant(limit) is { } value:
                return Narrow(state, mirrored.Local, holds ? Requirement.Interval(Mirrored(mirroredKind), value) : Requirement.Interval(Mirrored(mirroredKind), value).Not());

            case IsPatternExpression { Scrutinee: LocalReferenceExpression listed, Pattern: ListPattern list } when holds:
                return Sliced(state, listed, list);

            case IsPatternExpression { Scrutinee: LocalReferenceExpression subject, Pattern: var pattern }
                when Requirement.Of(pattern) is { Bounds.Length: 0 } matched:
                return Narrow(state, subject.Local, holds ? matched.Values : matched.Values.Not());

            default:
                return state;
        }
    }

    private static ImmutableDictionary<object, Knowledge> Narrow(
        ImmutableDictionary<object, Knowledge> state, LocalVariable local, Admitted values)
    {
        var known = state.TryGetValue(local, out var existing) ? existing : Knowledge.Unknown;
        return state.SetItem(local, known with { Values = known.Values.And(values) });
    }

    private static ImmutableDictionary<object, Knowledge> Given(
        ImmutableDictionary<object, Knowledge> state, LocalVariable local, Expression? value)
    {
        if(value is null)
        {
            return state.Remove(local);
        }

        if(Requirement.Constant(value) is { } constant)
        {
            return state.SetItem(local, new Knowledge(Admitted.Exactly(constant), ImmutableHashSet<Bound>.Empty));
        }

        if(value is LocalReferenceExpression source && state.TryGetValue(source.Local, out var copied))
        {
            return state.SetItem(local, copied);
        }

        return Lengths.Of(value, held => Held(state, held)) is { } length
            ? state.SetItem(local, Knowledge.Unknown with { Length = length })
            : state.Remove(local)
            ;
    }

    private static ImmutableDictionary<object, Knowledge> Forget(ImmutableDictionary<object, Knowledge> state, Func<object, bool> lost)
    {
        foreach(var (other, known) in state)
        {
            if(other is Field && lost(other))
            {
                state = state.Remove(other);
            }
            else if(known.Bounds.Any(bound => lost(bound.Holder)))
            {
                state = state.SetItem(other, known with { Bounds = known.Bounds.Where(bound => !lost(bound.Holder)).ToImmutableHashSet() });
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

    private static ImmutableDictionary<object, Knowledge> Sized(
        ImmutableDictionary<object, Knowledge> state, object measured, Admitted admitted, bool holds)
    {
        var known = state.TryGetValue(measured, out var existing) ? existing : Knowledge.Unknown;
        var learned = holds ? admitted : admitted.Not();

        return state.SetItem(measured, known with { Length = known.Length is { } already ? already.And(learned) : learned });
    }

    public static Admitted? Held(ImmutableDictionary<object, Knowledge> state, Expression? receiver)
    {
        return Holder(receiver) is { } holder && state.TryGetValue(holder, out var known)
            ? known.Length
            : null
            ;
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
            BinaryOperatorKind.Equal => ComparisonKind.Equal,
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