using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Numerics;
using System.Runtime.CompilerServices;
using Mist.ProgramAnalysis.Facts;
using Mist.ProgramAnalysis.Refinements;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.ProgramAnalysis.Rules;

internal sealed record Bound(object Holder, ComparisonKind Kind, IntegralInterval? Domain = null);

internal sealed record Knowledge(Admitted Values, ImmutableHashSet<Bound> Bounds, int Changes = 0, Admitted? Length = null, StoredRange? Slice = null, bool RefusedSlice = false, LiteralValue? Literal = null, RangeBound? IndexValue = null, RealSet? Real = null, int RealChanges = 0, int LowerChanges = 0, int UpperChanges = 0)
{
    public static readonly Knowledge Unknown = new(Admitted.Everything, ImmutableHashSet<Bound>.Empty);
}

internal sealed record FactState(Bindings Bindings, Bag Bag, ImmutableDictionary<Value, StoredRange> Ranges)
{
    private static ConditionalWeakTable<Bag, ConcurrentDictionary<Value, IntegralSet>> ProjectedIntervals { get; } = new();
    public static readonly FactState Empty = new(Bindings.Empty, Bag.Empty, ImmutableDictionary<Value, StoredRange>.Empty);

    public ImmutableDictionary<Value, Knowledge> Details { get; init; } = ImmutableDictionary<Value, Knowledge>.Empty;
    public ImmutableDictionary<object, Term> Results { get; init; } = ImmutableDictionary<object, Term>.Empty;
    public ImmutableDictionary<CompoundAssignmentExpression, Term> CompoundInputs { get; init; } = ImmutableDictionary<CompoundAssignmentExpression, Term>.Empty;
    public IReadOnlySet<object> Unseen { get; init; } = UnseenWrites.None;
    public ImmutableDictionary<LocalVariable, Expression> PatternSources { get; init; } = ImmutableDictionary<LocalVariable, Expression>.Empty;
    public bool IsSummary { get; init; }

    public bool TryGetValue(object holder, out Knowledge known)
    {
        if(!Unseen.Contains(holder) && Bindings.Get(holder) is { } value)
        {
            known = Details.GetValueOrDefault(value) ?? Knowledge.Unknown;
            known = known with { Values = IntegralTypes.Range(value.Type) is not null || value.Type.FullyQualifiedName == "Standard.Boolean" ? AdmittedValues(Intervals(value)) : known.Values, Slice = Ranges.GetValueOrDefault(value) };
            return true;
        }

        known = Knowledge.Unknown;
        return false;
    }

    public IntegralSet Intervals(Value value)
    {
        var cached = ProjectedIntervals.GetValue(Bag, _ => new ConcurrentDictionary<Value, IntegralSet>());
        return cached.GetOrAdd(value, current => Project(Bag, current));
    }

    private static IntegralSet Project(Bag bag, Value value)
    {
        if(bag.Unreachable)
        {
            return IntegralSet.Nothing;
        }

        var values = IntegralSet.Nothing;
        var meter = new ProofBudget(Budget.Default);
        foreach(var alternative in bag.Alternatives)
        {
            var facts = bag.Common.Concat(alternative.Facts).ToList();
            if(!facts.Any(fact => fact.Term.Coefficients.Count > 1 && fact.Term.Coefficients.ContainsKey(value)))
            {
                values = values.Or(Chain.SingleValue(facts, value));
                continue;
            }

            if(Chain.ProjectIntervals(facts, value, meter) is not { } projected)
            {
                return IntegralSet.Everything;
            }

            values = values.Or(projected);
        }

        return values;
    }

    public FactState SetItem(object holder, Knowledge known)
    {
        if(Bindings.Get(holder) is not { } value || Unseen.Contains(holder))
        {
            return this;
        }

        return this with
        {
            Details = Details.SetItem(value, known),
            Ranges = known.Slice is { } slice ? Ranges.SetItem(value, slice) : Ranges.Remove(value),
        };
    }

    public static Admitted AdmittedValues(IntegralSet values)
    {
        if(values.Intervals.Any(interval => interval.Low < long.MinValue || interval.High > long.MaxValue))
        {
            return Admitted.Everything;
        }

        var admitted = Admitted.Nothing;
        foreach(var interval in values.Intervals)
        {
            if(interval.Low > long.MaxValue || interval.High < long.MinValue)
            {
                continue;
            }

            var low = interval.Low is { } lower ? (long)BigInteger.Max(lower, long.MinValue) : long.MinValue;
            var high = interval.High is { } upper ? (long)BigInteger.Min(upper, long.MaxValue) : long.MaxValue;
            admitted = admitted.Or(Admitted.Between(low, high));
        }

        return admitted;
    }
}