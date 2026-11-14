using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Contracts;

public interface ITypeParameterConstraint
{
    TypeParameterConstraintKind Kind { get; }
}

public interface ITypeConstraint : ITypeParameterConstraint
{
    IMistType ConstraintType { get; }
}

/// <summary>
/// <c>where T : new(ParamTypes...)</c>; a bare <c>new()</c> leaves the parameter types empty.
/// </summary>
public interface IConstructorConstraint : ITypeParameterConstraint
{
    IReadOnlyList<IMistType> ParameterTypes { get; }
}