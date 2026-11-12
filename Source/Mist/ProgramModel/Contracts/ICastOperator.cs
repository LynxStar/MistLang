using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// An implicit or explicit conversion: <see cref="TargetType"/> is the return type and
/// <see cref="SourceParameter"/> the single parameter.
/// </summary>
public interface ICastOperator : IMethod
{
    IMistType TargetType { get; }

    IParameter SourceParameter { get; }

    bool IsImplicit { get; }
}