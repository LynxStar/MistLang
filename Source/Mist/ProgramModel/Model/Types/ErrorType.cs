using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Sentinel type representing an unresolved or invalid type. Prevents
/// cascading diagnostics by allowing downstream code to proceed with a
/// known-bad type rather than null.
/// </summary>
internal sealed class ErrorType : MistType, IMistType
{
    public static readonly ErrorType Instance = new();

    private ErrorType()
        : base("<error>", "", new Scope(null), declaringType: null)
    {
    }
}