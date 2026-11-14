using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The type of a name that resolved to overloads, before the context it appears in constrains them
/// to one.
/// <para>
/// Alone among the types here it describes no value. No Mist syntax produces a group as a value
/// (<c>var x = IsPositive;</c> is circular: <c>var</c> wants the initialiser's type and the initialiser
/// wants a target), so this is unreachable from source and there is nothing to emit it as.
/// </para>
/// <para>
/// Kept distinct from <see cref="ErrorType"/> because the two want opposite treatment downstream.
/// An error type means a diagnostic was already reported and nothing further should be said; this
/// means the binder failed to consume something it was required to consume, which is a compiler bug
/// rather than anything wrong with the source.
/// </para>
/// </summary>
internal sealed class MethodGroupType : MistType, IMistType
{
    public static readonly MethodGroupType Instance = new();

    private MethodGroupType()
        : base("<method group>", new Scope(null), declaringType: null)
    {
    }
}