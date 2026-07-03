using Mist.ProgramModel.Contracts.Types;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// Sentinel type representing the unknown top type. All non-void values
/// are assignable to it. Used for gradual typing and inference boundaries.
/// </summary>
internal sealed class UnknownType : MistType, IMistType
{
    public static readonly UnknownType Instance = new();

    private UnknownType()
        : base("unknown", "", new Scope(null), declaringType: null)
    {
    }
}