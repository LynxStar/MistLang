using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel.Model.Types;

/// <summary>
/// The type of a name that resolved to overloads, before the context it appears in constrains them
/// to one.
/// <para>
/// Alone among the types here it describes no value. Resolving a group happens entirely at compile
/// time and leaves nothing behind, and no Mist syntax can produce one as a value:
/// <c>var x = IsPositive;</c> is circular, since <c>var</c> wants the initialiser's type and the
/// initialiser wants a target. So this is unreachable from source and cannot be emitted, because
/// there is nothing to emit it as.
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