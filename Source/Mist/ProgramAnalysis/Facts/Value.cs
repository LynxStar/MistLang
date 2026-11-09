using System.Collections.Immutable;
using Mist.ProgramModel.Contracts.Types;
using Storm.Runtime;

namespace Mist.ProgramAnalysis.Facts;

internal sealed class Value(IMistType type, string displayName, SyntaxNode node)
{
    private static long nextId;

    internal long Id { get; } = Interlocked.Increment(ref nextId);
    public IMistType Type { get; } = type;
    public string DisplayName { get; } = displayName;
    public SyntaxNode Node { get; } = node;

    public override string ToString()
    {
        return DisplayName;
    }
}

internal sealed record MemberHolder(Value Receiver, object Member);

internal sealed record Bindings
{
    public static readonly Bindings Empty = new();

    public ImmutableDictionary<object, Value> Values { get; init; } = ImmutableDictionary<object, Value>.Empty;
    public ImmutableArray<object> Order { get; init; } = [];

    public Value? Get(object holder)
    {
        return Values.GetValueOrDefault(holder);
    }

    public Bindings Set(object holder, Value value)
    {
        return this with { Values = Values.SetItem(holder, value), Order = Values.ContainsKey(holder) ? Order : Order.Add(holder) };
    }

    public (Bindings Bindings, Value Value) Read(object holder, IMistType type, string name, SyntaxNode node)
    {
        if(Get(holder) is { } existing)
        {
            return (this, existing);
        }

        var value = new Value(type, name, node);
        return (Set(holder, value), value);
    }

    public Bindings Remove(IEnumerable<object> holders)
    {
        var removed = holders.ToHashSet();
        return this with { Values = Values.RemoveRange(removed), Order = [.. Order.Where(holder => !removed.Contains(holder))] };
    }
}