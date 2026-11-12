using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Model;

internal abstract class TypeParameterConstraint : ITypeParameterConstraint
{
    public abstract TypeParameterConstraintKind Kind { get; }
}

internal sealed class KeywordConstraint : TypeParameterConstraint
{
    public KeywordConstraint(TypeParameterConstraintKind kind)
    {
        Kind = kind;
    }

    public override TypeParameterConstraintKind Kind { get; }
}

internal sealed class TypeConstraint : TypeParameterConstraint, ITypeConstraint
{
    public TypeConstraint(IMistType constraintType)
    {
        ConstraintType = constraintType;
    }

    public override TypeParameterConstraintKind Kind => TypeParameterConstraintKind.Type;

    public IMistType ConstraintType { get; }
}

internal sealed class ConstructorConstraint : TypeParameterConstraint, IConstructorConstraint
{
    public ConstructorConstraint(IReadOnlyList<IMistType> parameterTypes)
    {
        ParameterTypes = parameterTypes;
    }

    public override TypeParameterConstraintKind Kind => TypeParameterConstraintKind.Constructor;

    public IReadOnlyList<IMistType> ParameterTypes { get; }
}