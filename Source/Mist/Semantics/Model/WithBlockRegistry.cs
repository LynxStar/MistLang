using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// A single validated member mapping from a <c>with</c> block.
/// Maps a source member on the implementing type to a target member on the trait.
/// </summary>
public sealed class ValidatedMemberMapping
{
    /// <summary>The source member symbol on the implementing type.</summary>
    public required ISymbol SourceMember { get; init; }

    /// <summary>The target member symbol on the trait.</summary>
    public required ISymbol TargetMember { get; init; }

    /// <summary>The source member name (from the <c>with</c> block).</summary>
    public required string SourceName { get; init; }

    /// <summary>The target member name (from the <c>with</c> block).</summary>
    public required string TargetName { get; init; }
}

/// <summary>
/// Stores validated <c>with</c> block mappings, keyed by (IType, TraitType) pairs.
/// Populated by <c>Pass1_ResolveWithBlocks</c> and consumed by the trait satisfaction pass.
/// </summary>
public sealed class WithBlockRegistry
{
    private readonly Dictionary<(IType Type, TraitType Trait), List<ValidatedMemberMapping>> _mappings = new();

    /// <summary>
    /// Registers a validated member mapping for a type-trait pair.
    /// </summary>
    public void AddMapping(IType type, TraitType trait, ValidatedMemberMapping mapping)
    {
        var key = (type, trait);
        if (!_mappings.TryGetValue(key, out var list))
        {
            list = new List<ValidatedMemberMapping>();
            _mappings[key] = list;
        }
        list.Add(mapping);
    }

    /// <summary>
    /// Returns <c>true</c> if a <c>with</c> block has been registered for the given type-trait pair.
    /// </summary>
    public bool HasMappings(IType type, TraitType trait)
        => _mappings.ContainsKey((type, trait));

    /// <summary>
    /// Returns the validated member mappings for a type-trait pair, or an empty list if none exist.
    /// </summary>
    public IReadOnlyList<ValidatedMemberMapping> GetMappings(IType type, TraitType trait)
        => _mappings.TryGetValue((type, trait), out var list)
            ? list
            : Array.Empty<ValidatedMemberMapping>();

    /// <summary>
    /// Returns all registered type-trait pairs and their mappings.
    /// </summary>
    public IEnumerable<(IType Type, TraitType Trait, IReadOnlyList<ValidatedMemberMapping> Mappings)> All()
        => _mappings.Select(kvp => (kvp.Key.Type, kvp.Key.Trait, (IReadOnlyList<ValidatedMemberMapping>)kvp.Value));
}
