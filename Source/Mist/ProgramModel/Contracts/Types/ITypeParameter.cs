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
    /// Whether this is a regular (type-carrying) parameter or a dependent
    /// (value-carrying) parameter.
    /// </summary>
    TypeParameterKind Kind { get; }

    /// <summary>
    /// For dependent type parameters, the resolved value type (e.g., <c>int</c>).
    /// Null for regular type parameters.
    /// </summary>
    IMistType? ValueType { get; }
}