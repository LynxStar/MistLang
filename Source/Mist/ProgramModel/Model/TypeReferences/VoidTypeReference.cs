using Mist.ProgramModel.Contracts.TypeReferences;

namespace Mist.ProgramModel.Model.TypeReferences;

/// <summary>
/// The void type (absence of a value).
/// </summary>
internal sealed class VoidTypeReference : IVoidTypeReference
{
    public static readonly VoidTypeReference Instance = new();

    private VoidTypeReference()
    {
    }

    public string DisplayName => "void";
}