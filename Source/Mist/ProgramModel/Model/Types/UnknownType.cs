using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The top type: every non-void value is assignable to it.
/// </summary>
internal sealed class UnknownType : MistType, IMistType
{
    public static readonly UnknownType Instance = new();

    private UnknownType() : base("unknown", new Scope(null), declaringType: null)
    {
    }
}