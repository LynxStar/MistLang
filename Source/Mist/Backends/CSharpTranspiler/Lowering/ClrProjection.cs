using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The Standard abstracts that ARE a CLR interface rather than merely resembling one. Mist declares
/// them so its own types can conform, and the backend spells every use as the CLR interface, because
/// <c>foreach</c>, LINQ and every framework API expect that one and a structurally identical
/// duplicate would satisfy none of them.
/// <para>
/// Which is also why such an abstract's associated type slot must NOT be lifted: the CLR interface's
/// arity is fixed by another assembly, so <see cref="SlotLift"/> appending a parameter would spell a
/// type that does not exist. The enumerator survives as <c>GetEnumerator</c>'s return type, where its
/// concrete struct is what keeps iteration unboxed.
/// </para>
/// </summary>
internal static class ClrProjection
{
    private static readonly Dictionary<string, string> ByFullyQualifiedName = new(StringComparer.Ordinal)
    {
        [WellKnownTypes.IEnumerable.FullyQualifiedName] = "System.Collections.Generic.IEnumerable",
        [WellKnownTypes.IEnumerator.FullyQualifiedName] = "System.Collections.Generic.IEnumerator",
    };

    /// <summary>
    /// The CLR interface a definition projects onto, without its type arguments, or null when it
    /// projects onto nothing and is emitted as Mist declared it.
    /// </summary>
    public static string? Name(IMistType definition)
    {
        return ByFullyQualifiedName.GetValueOrDefault(definition.FullyQualifiedName);
    }

    public static bool Projects(IMistType type)
    {
        return Name(SlotLift.Definition(type)) is not null;
    }
}