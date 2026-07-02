using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model;

/// <summary>
/// A cast operator (implicit or explicit conversion) declared on a type.
/// </summary>
internal sealed class CastOperatorDefinition(
    ITypeReference targetType,
    IParameterDefinition sourceParameter,
    bool isImplicit) : ICastOperatorDefinition
{
    public ITypeReference TargetType { get; } = targetType;

    public IParameterDefinition SourceParameter { get; } = sourceParameter;

    public bool IsImplicit { get; } = isImplicit;
}