using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// The unknown top type (all non-void values are assignable to it).
/// </summary>
internal sealed class UnknownTypeReference : IUnknownTypeReference
{
    public static readonly UnknownTypeReference Instance = new();

    private UnknownTypeReference()
    {
    }

    public string DisplayName => "unknown";
}