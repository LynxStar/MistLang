using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A constraint declared on a type parameter. Keyword constraints (class, struct,
/// readonly, etc.) are represented by <see cref="KeywordConstraint"/>. Constraints
/// that carry additional type information use specialized subtypes.
/// </summary>
internal abstract class TypeParameterConstraint : ITypeParameterConstraint
{
    public abstract TypeParameterConstraintKind Kind { get; }
}

/// <summary>
/// A keyword constraint with no additional data: class, struct, readonly, mutable,
/// owned, linear, affine, relevant, default.
/// </summary>
internal sealed class KeywordConstraint : TypeParameterConstraint
{
    public KeywordConstraint(TypeParameterConstraintKind kind)
    {
        Kind = kind;
    }

    public override TypeParameterConstraintKind Kind { get; }
}

/// <summary>
/// A type constraint (<c>where T : SomeType</c>). The type parameter must
/// conform to the referenced type.
/// </summary>
internal sealed class TypeConstraint : TypeParameterConstraint, ITypeConstraint
{
    public TypeConstraint(IMistType constraintType)
    {
        ConstraintType = constraintType;
    }

    public override TypeParameterConstraintKind Kind => TypeParameterConstraintKind.Type;

    public IMistType ConstraintType { get; }
}

/// <summary>
/// A constructor constraint (<c>where T : new(ParamTypes...)</c>).
/// </summary>
internal sealed class ConstructorConstraint : TypeParameterConstraint, IConstructorConstraint
{
    public ConstructorConstraint(IReadOnlyList<IMistType> parameterTypes)
    {
        ParameterTypes = parameterTypes;
    }

    public override TypeParameterConstraintKind Kind => TypeParameterConstraintKind.Constructor;

    public IReadOnlyList<IMistType> ParameterTypes { get; }
}