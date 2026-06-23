using Mist.Semantics.Contracts;
using Mist.Semantics.Model.Types;

namespace Mist.Semantics.Model;

/// <summary>
/// Describes how a type conforms to an abstract type (or trait, during migration).
/// </summary>
public sealed class ConformanceRecord
{
    /// <summary>
    /// Members filled by the implementing type (name → symbol).
    /// For nominal abstract type conformance this records slot-filling members.
    /// </summary>
    public IReadOnlyDictionary<string, ISymbol> FilledSlots { get; init; }
        = new Dictionary<string, ISymbol>();

    /// <summary>
    /// Members filled via with-block mapping (name → source symbol).
    /// </summary>
    public IReadOnlyDictionary<string, ISymbol> WithMappings { get; init; }
        = new Dictionary<string, ISymbol>();

    /// <summary>
    /// Methods that use the default implementation (name set).
    /// Empty for traits (which have no defaults); populated for abstract types.
    /// </summary>
    public IReadOnlySet<string> DefaultsUsed { get; init; }
        = new HashSet<string>();
}

/// <summary>
/// Records which types conform to which abstract types (or traits, during migration),
/// and how (slot filling or with-block mapping). No structural matching is performed;
/// conformance requires explicit nominal declaration or a with block.
/// Populated by <c>Pass1_ValidateAbstractSatisfaction</c> and consumed by the type checker
/// and emitter.
/// </summary>
public sealed class ConformanceMap
{
    /// <summary>
    /// Key: (implementingType, abstractType/trait). Value: the conformance record.
    /// The abstract type key is <see cref="IType"/> to support both <see cref="AbstractType"/>
    /// (non-generic), <see cref="ConstructedAbstractType"/> (generic), and future
    /// <see cref="AbstractType"/> types.
    /// </summary>
    private readonly Dictionary<(IType, IType), ConformanceRecord> _conformances = new();

    public void Register(IType type, IType abstractType, ConformanceRecord record)
        => _conformances[(type, abstractType)] = record;

    public bool Conforms(IType type, IType abstractType)
    {
        if (_conformances.ContainsKey((type, abstractType)))
            return true;

        // Structural equality fallback for ConstructedAbstractType:
        // Pass 2 type resolution creates new ConstructedAbstractType instances
        // that are not reference-equal to the ones registered during Pass 1.
        // Fall back to structural comparison (same GenericAbstractDefinition +
        // same type arguments by DisplayName).
        if (abstractType is ConstructedAbstractType queryCtt)
        {
            foreach (var key in _conformances.Keys)
            {
                if (!ReferenceEquals(key.Item1, type))
                    continue;
                if (key.Item2 is ConstructedAbstractType registeredCtt
                    && ReferenceEquals(registeredCtt.GenericAbstractDefinition, queryCtt.GenericAbstractDefinition)
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

    public ConformanceRecord? GetConformance(IType type, IType abstractType)
    {
        if (_conformances.TryGetValue((type, abstractType), out var record))
            return record;

        // Structural equality fallback for ConstructedAbstractType (same as Conforms).
        if (abstractType is ConstructedAbstractType queryCtt)
        {
            foreach (var kvp in _conformances)
            {
                if (!ReferenceEquals(kvp.Key.Item1, type))
                    continue;
                if (kvp.Key.Item2 is ConstructedAbstractType registeredCtt
                    && ReferenceEquals(registeredCtt.GenericAbstractDefinition, queryCtt.GenericAbstractDefinition)
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

    public IEnumerable<(IType Type, IType Abstract, ConformanceRecord Record)> All()
        => _conformances.Select(kvp => (kvp.Key.Item1, kvp.Key.Item2, kvp.Value));

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
