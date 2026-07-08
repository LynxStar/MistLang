namespace Mist.ProgramModel.Contracts.Types;

/// <summary>
/// A type parameter declared on a generic type (or eventually a generic method).
/// Type parameters are themselves types — they can appear anywhere a type reference
/// resolves (base clauses, field types, parameter types, etc.).
/// </summary>
public interface ITypeParameter : IMistType
{
    /// <summary>
    /// The zero-based positional index of this type parameter within its declaring
    /// type's parameter list.
    /// </summary>
    int Ordinal { get; }

    /// <summary>
    /// The domain of this type parameter — the type whose values can fill this slot.
    /// For regular type parameters (<c>T</c>), this is <c>Standard.Type</c>.
    /// For dependent type parameters (<c>N:int</c>), this is the annotated value type.
    /// Null until resolved in Phase 2.
    /// </summary>
    IMistType? Domain { get; }

    /// <summary>
    /// The constraints declared on this type parameter via where clauses.
    /// Empty until resolved in Phase 2.
    /// </summary>
    IReadOnlyList<ITypeParameterConstraint> Constraints { get; }
}