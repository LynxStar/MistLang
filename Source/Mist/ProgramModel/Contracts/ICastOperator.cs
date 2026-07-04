using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A cast operator (implicit or explicit conversion) declared on a type.
/// Cast operators are methods whose return type is the target type and whose
/// single parameter is the source value.
/// </summary>
public interface ICastOperator : IMethod
{
    /// <summary>The target type of the cast (same as ReturnType).</summary>
    IMistType TargetType { get; }

    /// <summary>The source parameter being converted (same as Parameters[0]).</summary>
    IParameter SourceParameter { get; }

    /// <summary>Whether this is an implicit conversion (true) or explicit (false).</summary>
    bool IsImplicit { get; }
}