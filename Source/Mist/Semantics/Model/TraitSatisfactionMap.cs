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
    /// Key: (implementingType, trait). Value: the satisfaction record.
    /// The trait key is <see cref="IType"/> to support both <see cref="TraitType"/>
    /// (non-generic) and <see cref="ConstructedTraitType"/> (generic).
    /// </summary>
    private readonly Dictionary<(IType, IType), TraitSatisfaction> _satisfactions = new();

    public void Register(IType type, IType trait, TraitSatisfaction satisfaction)
        => _satisfactions[(type, trait)] = satisfaction;

    public bool Satisfies(IType type, IType trait)
    {
        if (_satisfactions.ContainsKey((type, trait)))
            return true;

        // Structural equality fallback for ConstructedTraitType:
        // Pass 2 type resolution creates new ConstructedTraitType instances
        // that are not reference-equal to the ones registered during Pass 1.
        // Fall back to structural comparison (same GenericTraitDefinition +
        // same type arguments by DisplayName).
        if (trait is ConstructedTraitType queryCtt)
        {
            foreach (var key in _satisfactions.Keys)
            {
                if (!ReferenceEquals(key.Item1, type))
                    continue;
                if (key.Item2 is ConstructedTraitType registeredCtt
                    && ReferenceEquals(registeredCtt.GenericTraitDefinition, queryCtt.GenericTraitDefinition)
                    && registeredCtt.TypeArguments.Count == queryCtt.TypeArguments.Count)
                {
                    var allMatch = true;
                    for (var i = 0; i < registeredCtt.TypeArguments.Count; i++)
                    {
                        if (!TypeArgumentDisplayNamesMatch(
                                registeredCtt.TypeArguments[i].DisplayName,
                                queryCtt.TypeArguments[i].DisplayName))
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (allMatch)
                        return true;
                }
            }
        }

        return false;
    }

    public TraitSatisfaction? GetSatisfaction(IType type, IType trait)
    {
        if (_satisfactions.TryGetValue((type, trait), out var satisfaction))
            return satisfaction;

        // Structural equality fallback for ConstructedTraitType (same as Satisfies).
        if (trait is ConstructedTraitType queryCtt)
        {
            foreach (var kvp in _satisfactions)
            {
                if (!ReferenceEquals(kvp.Key.Item1, type))
                    continue;
                if (kvp.Key.Item2 is ConstructedTraitType registeredCtt
                    && ReferenceEquals(registeredCtt.GenericTraitDefinition, queryCtt.GenericTraitDefinition)
                    && registeredCtt.TypeArguments.Count == queryCtt.TypeArguments.Count)
                {
                    var allMatch = true;
                    for (var i = 0; i < registeredCtt.TypeArguments.Count; i++)
                    {
                        if (!TypeArgumentDisplayNamesMatch(
                                registeredCtt.TypeArguments[i].DisplayName,
                                queryCtt.TypeArguments[i].DisplayName))
                        {
                            allMatch = false;
                            break;
                        }
                    }
                    if (allMatch)
                        return kvp.Value;
                }
            }
        }

        return null;
    }

    public IEnumerable<(IType Type, IType Trait, TraitSatisfaction Satisfaction)> All()
        => _satisfactions.Select(kvp => (kvp.Key.Item1, kvp.Key.Item2, kvp.Value));

    /// <summary>
    /// Compares two type argument display names, accounting for the keyword-to-stdlib
    /// name mapping (e.g., "int" matches "Int32", "string" matches "String").
    /// This is needed because with-block resolution (Pass 1g) uses legacy primitive
    /// types with keyword display names, while Pass 2 resolves to stdlib types with
    /// their canonical names.
    /// </summary>
    private static bool TypeArgumentDisplayNamesMatch(string a, string b)
    {
        if (a == b)
            return true;

        // Don't match empty or null names — they indicate unresolved types.
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return false;

        // Check if one is a keyword and the other is the stdlib name.
        return NormalizeTypeName(a) == NormalizeTypeName(b);
    }

    /// <summary>
    /// Normalizes a type display name by mapping primitive keywords to their
    /// stdlib simple names (e.g., "int" → "Int32").
    /// </summary>
    private static string NormalizeTypeName(string name)
    {
        if (KeywordAliases.Map.TryGetValue(name, out var qualifiedName))
        {
            var dotIndex = qualifiedName.LastIndexOf('.');
            return dotIndex >= 0 ? qualifiedName[(dotIndex + 1)..] : qualifiedName;
        }
        return name;
    }
}
