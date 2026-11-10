using System.Collections.Immutable;

namespace Mist.ProgramAnalysis.Facts;

internal sealed record FactIndex(ImmutableDictionary<Value, ImmutableArray<int>> Mentions, ImmutableDictionary<Value, IntegralSet> Bounds, ImmutableArray<int> Constants)
{
    public static FactIndex Of(ImmutableArray<Fact> common)
    {
        var mentions = new Dictionary<Value, List<int>>();
        var bounds = new Dictionary<Value, IntegralSet>();
        var constants = ImmutableArray.CreateBuilder<int>();
        for(var position = 0; position < common.Length; position++)
        {
            var fact = common[position];
            if(fact.Term.Variables.IsEmpty)
            {
                constants.Add(position);
            }

            foreach(var value in fact.Term.Variables)
            {
                if(!mentions.TryGetValue(value, out var positions))
                {
                    positions = [];
                    mentions.Add(value, positions);
                }

                positions.Add(position);
            }

            if(fact.Term.Variables is [var single])
            {
                bounds[single] = bounds.GetValueOrDefault(single, IntegralSet.Everything).And(Chain.SingleValue([fact], single));
            }
        }

        return new FactIndex(mentions.ToImmutableDictionary(pair => pair.Key, pair => pair.Value.ToImmutableArray()), bounds.ToImmutableDictionary(),
            constants.ToImmutable());
    }

    public ImmutableArray<int> Of(Value value)
    {
        return Mentions.GetValueOrDefault(value, []);
    }

    public IntegralSet Bound(Value value)
    {
        return Bounds.GetValueOrDefault(value) ?? IntegralSet.Everything;
    }

    public FactIndex With(Fact fact, int position)
    {
        var mentions = Mentions;
        foreach(var value in fact.Term.Variables)
        {
            mentions = mentions.SetItem(value, Of(value).Add(position));
        }

        return new FactIndex(mentions, fact.Term.Variables is [var single] ? Bounds.SetItem(single, Bound(single).And(Chain.SingleValue([fact], single))) : Bounds,
            fact.Term.Variables.IsEmpty ? Constants.Add(position) : Constants);
    }
}