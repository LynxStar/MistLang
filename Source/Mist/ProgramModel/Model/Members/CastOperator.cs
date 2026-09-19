using Mist.Common.Diagnostics;
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
    /// the implicit one is lossless, so <see cref="Construction.Binding.ConversionRelation"/> reads it as
    /// containment wherever C# carries it unaided.
    /// </summary>
    public bool CopiesClone { get; init; }

    public Method? ClonesThrough { get; init; }

    public Diagnostic? BindingFailure { get; internal set; }

    /// <summary>
    /// Whether a cast calls this operator. A conversion run through <see cref="ClonesThrough"/> is refused
    /// only where a cast calls it (newtypes spec Requirement 10.4), so one no cast calls may hold a body
    /// that does not type, and is not a member any backend can write.
    /// </summary>
    public bool IsCalled { get; internal set; }

    IParameter ICastOperator.SourceParameter => SourceParameter;
}