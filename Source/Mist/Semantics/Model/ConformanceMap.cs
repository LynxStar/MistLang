using Mist.Semantics.Contracts;

namespace Mist.Semantics.Model;

/// <summary>
/// A validated member mapping from a with-block.
/// Maps a source member on the adapting type to a slot on the abstract type.
/// </summary>
public sealed class ConformanceMapping
{
    public required ISymbol SourceMember { get; init; }
    public required ISymbol TargetSlot { get; init; }
    public required string SourceName { get; init; }
    public required string TargetName { get; init; }
}

/// <summary>
/// Records with-block conformance registrations.
/// Key: (concreteType, abstractType). Value: list of member mappings.
/// </summary>
public sealed class ConformanceMap
{
    private readonly Dictionary<(IType, IType), List<ConformanceMapping>> _entries = new();

    /// <summary>
    /// Registers a member mapping for a with-block conformance between a concrete type
    /// and an abstract type.
    /// </summary>
    public void Register(IType type, IType abstractType, ConformanceMapping mapping)
    {
        var key = (type, abstractType);
        if(!_entries.TryGetValue(key, out var list))
        {
            list = new List<ConformanceMapping>();
            _entries[key] = list;
        }

        list.Add(mapping);
    }

    /// <summary>
    /// Returns true if a with-block conformance has been registered between the given
    /// concrete type and abstract type.
    /// </summary>
    public bool HasConformance(IType type, IType abstractType) => _entries.ContainsKey((type, abstractType));

    /// <summary>
    /// Returns the list of member mappings for the given conformance pair,
    /// or an empty list if no conformance has been registered.
    /// </summary>
    public IReadOnlyList<ConformanceMapping> GetMappings(IType type, IType abstractType)
    {
        if(_entries.TryGetValue((type, abstractType), out var list))
        {
            return list;
        }

        return Array.Empty<ConformanceMapping>();
    }

    /// <summary>
    /// Enumerates all registered conformance entries.
    /// </summary>
    public IEnumerable<(IType Type, IType AbstractType, IReadOnlyList<ConformanceMapping> Mappings)> All()
    {
        foreach(var ((type, abstractType), mappings) in _entries)
        {
            yield return (type, abstractType, mappings);
        }
    }
}