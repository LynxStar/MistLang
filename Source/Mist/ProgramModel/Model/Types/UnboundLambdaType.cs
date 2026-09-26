using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The type of a lambda argument before its call has chosen the parameter types it gets. Like
/// <see cref="MethodGroupType"/> it describes no value and cannot be emitted, so one that outlives
/// binding is the binder's failure rather than the program's.
/// </summary>
internal sealed class UnboundLambdaType : MistType, IMistType
{
    public static readonly UnboundLambdaType Instance = new();

    private UnboundLambdaType()
        : base("<lambda>", new Scope(null), declaringType: null)
    {
    }
}