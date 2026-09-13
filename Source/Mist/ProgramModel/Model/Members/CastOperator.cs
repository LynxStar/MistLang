using Mist.ProgramModel.Contracts;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Members;

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

    /// <summary>
    /// A conversion between a newtype's clone and its copyable source, synthesized rather than declared
    /// (newtypes spec Requirements 2.4 and 2.5). It has no body: the copy is the backend's to write, and
    /// the implicit one is lossless, so the conversion relation reads it as containment.
    /// </summary>
    public bool CopiesClone { get; init; }

    IParameter ICastOperator.SourceParameter => SourceParameter;
}