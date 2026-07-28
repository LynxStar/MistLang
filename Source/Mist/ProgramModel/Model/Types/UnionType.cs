using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// A union type composed of multiple resolved constituent types.
/// Not registered in a namespace — constructed on-the-fly during type resolution.
/// </summary>
internal sealed class UnionType : MistType, IUnionType
{
    public UnionType(IReadOnlyList<IMistType> constituents)
        : base(BuildName(constituents), "", new Scope(null), declaringType: null)
    {
        Constituents = constituents;
    }

    /// <summary>
    /// The resolved constituent types of the union.
    /// </summary>
    public IReadOnlyList<IMistType> Constituents { get; }

    IReadOnlyList<IMistType> IUnionType.Constituents => Constituents;

    private static string BuildName(IReadOnlyList<IMistType> constituents)
    {
        return string.Join(" | ", constituents.Select(c => c.Name));
    }
}