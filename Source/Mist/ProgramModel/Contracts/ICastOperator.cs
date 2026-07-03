using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Contracts;

/// <summary>
/// A cast operator (implicit or explicit conversion) declared on a type.
/// </summary>
public interface ICastOperator
{
    /// <summary>The target type of the cast.</summary>
    ITypeReference TargetType { get; }

    /// <summary>The source parameter being converted.</summary>
    IParameter SourceParameter { get; }

    /// <summary>Whether this is an implicit conversion (true) or explicit (false).</summary>
    bool IsImplicit { get; }
}