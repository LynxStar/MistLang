using System.Collections.Immutable;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class OwnershipFlow
{
    private sealed record ReferenceValue(Place Storage);

    // Values retain their identity when a holder changes; installations belong to scopes, not names.
    private static ImmutableHashSet<object> ValuesOf(ImmutableDictionary<Place, ImmutableHashSet<object>> values, Place place)
    {
        return ValuesOf(values, place, []);
    }

    private static ImmutableHashSet<object> ValuesOf(ImmutableDictionary<Place, ImmutableHashSet<object>> values, Place place, HashSet<Place> visiting)
    {
        if(!visiting.Add(place))
        {
            return [];
        }

        var found = values.TryGetValue(place, out var held) ? held : ImmutableHashSet.Create<object>(place.Root);
        var result = found.Where(value => value is not ReferenceValue).ToImmutableHashSet();

        foreach(var reference in found.OfType<ReferenceValue>())
        {
            result = result.Union(ValuesOf(values, reference.Storage, visiting));
        }

        if(NonOwningKind(place.Root) is not null)
        {
            result = result.Add(place.Root);
        }

        _ = visiting.Remove(place);
        return result;
    }

    private static ImmutableDictionary<TKey, ImmutableHashSet<object>> MergeValues<TKey>(
        ImmutableDictionary<TKey, ImmutableHashSet<object>> left, ImmutableDictionary<TKey, ImmutableHashSet<object>> right) where TKey : notnull
    {
        foreach(var (key, values) in right)
        {
            left = left.SetItem(key, left.TryGetValue(key, out var prior) ? prior.Union(values) : values);
        }

        return left;
    }

    private static ImmutableDictionary<Place, ImmutableHashSet<object>> JoinValues(
        ImmutableDictionary<Place, ImmutableHashSet<object>> left, ImmutableDictionary<Place, ImmutableHashSet<object>> right)
    {
        return left.Keys.Union(right.Keys).ToImmutableDictionary(place => place, place =>
            (left.TryGetValue(place, out var l) ? l : ImmutableHashSet.Create<object>(place.Root))
            .Union(right.TryGetValue(place, out var r) ? r : ImmutableHashSet.Create<object>(place.Root)));
    }

    private static bool SameValues<TKey>(ImmutableDictionary<TKey, ImmutableHashSet<object>> left,
        ImmutableDictionary<TKey, ImmutableHashSet<object>> right) where TKey : notnull
    {
        return left.Count == right.Count && left.All(pair => right.TryGetValue(pair.Key, out var found) && pair.Value.SetEquals(found));
    }

    private static State BindValue(State state, LocalVariable local, Expression? source, object fresh, bool assignment = false)
    {
        List<Place> places = source is null
            ? []
            : OwnershipAnalyzer.Selected(source).Select(leaf => Place.Of(leaf)).OfType<Place>().ToList();

        var values = places.Count == 0
            ? ImmutableHashSet.Create(fresh)
            : local.IsRef && !assignment && places is [var single]
                ? ImmutableHashSet.Create<object>(new ReferenceValue(single))
                : places.Aggregate(ImmutableHashSet<object>.Empty, (set, place) => set.Union(ValuesOf(state.Values, place)));

        if(assignment && local.IsRef && state.Values.TryGetValue(Place.Of(local), out var targets))
        {
            foreach(var reference in targets.OfType<ReferenceValue>())
            {
                state = WriteReference(state, reference.Storage, values, []);
            }

            return state;
        }

        return Forget(state, local) with { Values = state.Values.SetItem(Place.Of(local), values) };
    }

    private static State WriteReference(State state, Place storage, ImmutableHashSet<object> values, HashSet<Place> visiting)
    {
        if(!visiting.Add(storage))
        {
            return state;
        }

        if(state.Values.TryGetValue(storage, out var prior) && prior.OfType<ReferenceValue>().Any())
        {
            foreach(var reference in prior.OfType<ReferenceValue>())
            {
                state = WriteReference(state, reference.Storage, values, visiting);
            }

            return state;
        }

        return Forget(state, storage.Root) with { Values = state.Values.SetItem(storage, values) };
    }

    private Expression? PatternSource(object pattern)
    {
        while(parents.TryGetValue(pattern, out var parent))
        {
            switch(parent)
            {
                case IsPatternExpression test:
                    return test.Scrutinee;
                case SwitchExpression choice:
                    return choice.Scrutinee;
                case PropertyPattern property when ReferenceEquals(property.TypeTest, pattern):
                    pattern = parent;
                    break;
                case TypeTestPattern or NegatedPattern or ConjunctionPattern or DisjunctionPattern or SwitchArm:
                    pattern = parent;
                    break;
                default:
                    return null;
            }
        }

        return null;
    }

    private State Install(State state, object enterNode)
    {
        if(scopes.EnteredAt(enterNode) is not { } scope)
        {
            return state;
        }

        var held = OwnershipAnalyzer.Resolved(scope.Value) is LocalReferenceExpression reference ? reference.Local : scope.Binding!;
        var place = Place.Of(held);
        var values = ValuesOf(state.Values, place);
        var active = state.Installed.Values.SelectMany(installed => installed).ToImmutableHashSet();
        var pinned = values.Where(value => !active.Contains(value) && value is not LocalVariable { IsContextBound: true } && !(value is LocalVariable local && NonOwningKind(local) is not null)).ToImmutableHashSet();

        object pin(object value)
        {
            return pinned.Contains(value) ? scope.Body : value;
        }

        var renamed = state.Values.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.Select(pin).ToImmutableHashSet());

        if(!renamed.ContainsKey(place))
        {
            renamed = renamed.SetItem(place, values.Select(pin).ToImmutableHashSet());
        }

        var loans = state.Leases.ToImmutableDictionary(pair => pair.Key,
            pair => pair.Value.Select(loan => loan with { Value = pin(loan.Value) }).ToImmutableHashSet());
        return state with
        {
            Values = renamed,
            Leases = loans,
            Installed = state.Installed.SetItem(scope.Body, values.Select(pin).ToImmutableHashSet()),
        };
    }

    private State LeaveInstallation(State state, Block body)
    {
        if(scopes.Leaving(body) is not { } scope || !state.Installed.TryGetValue(body, out var released))
        {
            return state;
        }

        var remaining = state.Installed.Remove(body);

        if(LifetimeOwners.IsView(scope.Value))
        {
            return state with { Installed = remaining };
        }

        var liveElsewhere = remaining.Values.SelectMany(values => values).ToImmutableHashSet();
        var places = state.Values.Keys.Concat(OwnershipAnalyzer.Resolved(scope.Value) is LocalReferenceExpression reference ? [Place.Of(reference.Local)] : Array.Empty<Place>());

        foreach(var place in places)
        {
            if(ValuesOf(state.Values, place).Any(value => released.Contains(value) && !liveElsewhere.Contains(value)))
            {
                state = state with { Moved = state.Moved.SetItem(place, new Moved(scope.Value, Dropped: true)) };
            }
        }

        return state with { Installed = remaining };
    }
}