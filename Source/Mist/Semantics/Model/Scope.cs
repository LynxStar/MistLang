using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// A concrete implementation of <see cref="IScope"/> with parent-chain lookup.
/// </summary>
public sealed class Scope : IScope
{
    private List<ISymbol> _members = [];
    private List<string> _importedNamespaces = [];

    public Scope(IScope? parent)
    {
        Parent = parent;
    }

    public IScope? Parent { get; }

    public IReadOnlyList<ISymbol> Members => _members;

    /// <summary>
    /// Namespaces imported via <c>using</c> declarations in this scope.
    /// </summary>
    public IReadOnlyList<string> ImportedNamespaces => _importedNamespaces;

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

    public IReadOnlyList<ISymbol> LookupAll(string name)
    {
        var results = new List<ISymbol>();
        CollectAll(name, results);
        return results;
    }

    private void CollectAll(string name, List<ISymbol> results)
    {
        foreach (var member in _members)
        {
            if (member.Name == name)
                results.Add(member);
        }

        if (Parent is Scope parentScope)
            parentScope.CollectAll(name, results);
        else if (Parent is not null)
        {
            var parentResults = Parent.LookupAll(name);
            results.AddRange(parentResults);
        }
    }

    internal void AddMember(ISymbol symbol)
    {
        _members.Add(symbol);
    }

    /// <summary>
    /// Records a namespace imported by a <c>using</c> declaration.
    /// </summary>
    internal void AddImportedNamespace(string namespaceName)
    {
        if (!_importedNamespaces.Contains(namespaceName))
        {
            _importedNamespaces.Add(namespaceName);
        }
    }
}
