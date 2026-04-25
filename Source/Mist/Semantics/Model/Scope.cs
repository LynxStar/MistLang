using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// A concrete implementation of <see cref="IScope"/> with parent-chain lookup.
/// </summary>
public sealed class Scope : IScope
{
    private List<ISymbol> _members = [];

    public Scope(IScope? parent)
    {
        Parent = parent;
    }

    public IScope? Parent { get; }

    public IReadOnlyList<ISymbol> Members => _members;

    public ISymbol? Lookup(string name)
    {
        foreach(var member in _members)
        {
            if(member.Name == name)
            {
                return member;
            }
        }

        return Parent?.Lookup(name);
    }

    internal void AddMember(ISymbol symbol)
    {
        _members.Add(symbol);
    }
}
