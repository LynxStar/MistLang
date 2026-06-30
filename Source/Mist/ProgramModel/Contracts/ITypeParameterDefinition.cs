namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A generic type parameter declared on a type or method.
/// </summary>
public interface ITypeParameterDefinition
{
    /// <summary>
    /// The name of the type parameter (e.g., "T", "TKey").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The ordinal position of this type parameter in the declaring type/method's
    /// parameter list (zero-based).
    /// </summary>
    int Ordinal { get; }

    /// <summary>
    /// Constraints on this type parameter (base type constraints, abstract constraints, etc.).
    /// </summary>
    IReadOnlyList<ITypeParameterConstraint> Constraints { get; }

    /// <summary>
    /// Whether this is a dependent type parameter (carries a value, not a type).
    /// </summary>
    bool IsDependent { get; }
}