using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// Describes how a type satisfies a trait.
/// </summary>
public sealed class TraitSatisfaction
{
    /// <summary>
    /// Members matched structurally (same name, compatible type).
    /// Key: trait member name. Value: the matching type member symbol.
    /// </summary>
    public IReadOnlyDictionary<string, ISymbol> StructuralMatches { get; init; }
        = new Dictionary<string, ISymbol>();

    /// <summary>
    /// Members matched via with-block mapping.
    /// Key: trait member name. Value: the source member symbol on the implementing type.
    /// </summary>
    public IReadOnlyDictionary<string, ISymbol> WithMappings { get; init; }
        = new Dictionary<string, ISymbol>();
}

/// <summary>
/// Records which types satisfy which traits, and how (structural vs with-block).
/// Populated by <c>Pass1_ResolveTraitSatisfaction</c> and consumed by the type
/// checker and emitter.
/// </summary>
public sealed class TraitSatisfactionMap
{
    /// <summary>
    /// Key: (implementingType, traitType). Value: the satisfaction record.
    /// </summary>
    private readonly Dictionary<(IType, TraitType), TraitSatisfaction> _satisfactions = new();

    public void Register(IType type, TraitType trait, TraitSatisfaction satisfaction)
        => _satisfactions[(type, trait)] = satisfaction;

    public bool Satisfies(IType type, TraitType trait)
        => _satisfactions.ContainsKey((type, trait));

    public TraitSatisfaction? GetSatisfaction(IType type, TraitType trait)
        => _satisfactions.GetValueOrDefault((type, trait));

    public IEnumerable<(IType Type, TraitType Trait, TraitSatisfaction Satisfaction)> All()
        => _satisfactions.Select(kvp => (kvp.Key.Item1, kvp.Key.Item2, kvp.Value));
}
