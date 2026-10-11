using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

internal sealed class UnboundContextualType : MistType
{
    public static readonly UnboundContextualType Instance = new();

    private UnboundContextualType()
        : base("<contextual expression>", new Scope(null), declaringType: null)
    {
    }
}