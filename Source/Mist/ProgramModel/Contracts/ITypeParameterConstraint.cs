using Mist.ProgramModel.Contracts.TypeReferences;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A constraint on a type parameter.
/// </summary>
public interface ITypeParameterConstraint
{
    /// <summary>
    /// The kind of constraint (base type, abstract implementation, etc.).
    /// </summary>
    TypeParameterConstraintKind ConstraintKind { get; }

    /// <summary>
    /// The type referenced by this constraint (the base type or abstract type).
    /// Null for non-type constraints.
    /// </summary>
    ITypeReference? ConstraintType { get; }
}