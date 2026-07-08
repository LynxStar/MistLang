using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A constraint declared on a type parameter via a where clause.
/// Constraints restrict which types can be substituted for the parameter.
/// </summary>
public interface ITypeParameterConstraint
{
    /// <summary>
    /// The kind of constraint (class, struct, new, type, etc.).
    /// </summary>
    TypeParameterConstraintKind Kind { get; }
}

/// <summary>
/// A type constraint (<c>where T : SomeType</c>). The type parameter must
/// implement or extend the specified type.
/// </summary>
public interface ITypeConstraint : ITypeParameterConstraint
{
    /// <summary>
    /// The type that the type parameter must conform to.
    /// </summary>
    IMistType ConstraintType { get; }
}

/// <summary>
/// A constructor constraint (<c>where T : new(ParamTypes...)</c>). The type parameter
/// must have a constructor with the specified parameter types.
/// </summary>
public interface IConstructorConstraint : ITypeParameterConstraint
{
    /// <summary>
    /// The parameter types required on the constructor. Empty for a parameterless
    /// constructor constraint (<c>new()</c>).
    /// </summary>
    IReadOnlyList<IMistType> ParameterTypes { get; }
}