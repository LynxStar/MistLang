using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A cast operator (implicit or explicit conversion) declared on a type.
/// </summary>
internal sealed class CastOperator(
    IMistType targetType,
    IParameter sourceParameter,
    bool isImplicit,
    MemberModifiers modifiers) : ICastOperator
{
    public IMistType TargetType { get; } = targetType;

    public IParameter SourceParameter { get; } = sourceParameter;

    public bool IsImplicit { get; } = isImplicit;

    public MemberModifiers Modifiers { get; } = modifiers;
}