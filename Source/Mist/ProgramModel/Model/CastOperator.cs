using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A cast operator (implicit or explicit conversion) declared on a type.
/// Cast operators are methods whose return type is the target type and whose
/// single parameter is the source value.
/// </summary>
internal sealed class CastOperator(
    IMistType targetType,
    Parameter sourceParameter,
    bool isImplicit,
    MemberModifiers modifiers)
    : Method(
        isImplicit ? $"op_Implicit_{targetType.Name}" : $"op_Explicit_{targetType.Name}",
        targetType,
        [sourceParameter],
        modifiers), ICastOperator
{
    public IMistType TargetType { get; } = targetType;

    public Parameter SourceParameter { get; } = sourceParameter;

    public bool IsImplicit { get; } = isImplicit;

    IParameter ICastOperator.SourceParameter => SourceParameter;
}