using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// An error placeholder for types that could not be resolved.
/// </summary>
internal sealed class ErrorTypeReference : IErrorTypeReference
{
    public static readonly ErrorTypeReference Instance = new();

    private ErrorTypeReference()
    {
    }

    public string DisplayName => "<error>";
}