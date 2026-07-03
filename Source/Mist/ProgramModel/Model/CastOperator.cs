using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A cast operator (implicit or explicit conversion) declared on a type.
/// </summary>
internal sealed class CastOperator(
    ITypeReference targetType,
    IParameter sourceParameter,
    bool isImplicit) : ICastOperator
{
    public ITypeReference TargetType { get; } = targetType;

    public IParameter SourceParameter { get; } = sourceParameter;

    public bool IsImplicit { get; } = isImplicit;
}