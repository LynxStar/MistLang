using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Patterns;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// A tuple pattern over a pack of open width, in the specialization being emitted. At the pattern's
/// own width the scrutinee is a C# tuple of that width and the pattern is the ordinary positional one.
/// At any other width C# would refuse that pattern outright, so the scrutinee is boxed and asked for
/// the tuple type of the pattern's width: one no value of this width can be, which still declares
/// every binding at the type the body gave it. An EMPTY pack is the exception, its tuple being the
/// closed <c>ValueTuple</c>, which C# knows is no other tuple type; there the boxed value is matched
/// positionally through <c>ITuple</c>, whose elements are the <c>object</c> every binding has there.
/// <para>
/// A pattern of nothing but bindings and discards matches every value at its own width, where C# then
/// refuses each later switch arm as unreachable. Those arms are unreachable in that specialization and
/// in no other, so it is only there that they are left out.
/// </para>
/// </summary>
internal static class PackTuplePatterns
{
    public static (bool Boxed, IMistType? Tested) Lowering(TuplePattern tuple)
    {
        if(tuple.OpenPack is not { } pack)
        {
            return (false, tuple.TestedType);
        }

        var width = Monomorph.InScope()?.Expansion(pack).Count ?? 0;

        if(width == tuple.Elements.Count)
        {
            return (false, null);
        }

        return (true, width == 0 ? null : tuple.TestedType);
    }

    public static IEnumerable<SwitchArm> Reached(IReadOnlyList<SwitchArm> arms)
    {
        foreach(var arm in arms)
        {
            yield return arm;

            if(arm is { Guard: null, Pattern: TuplePattern { OpenPack: not null } tuple }
                && Lowering(tuple) is (false, null)
                && tuple.Elements.All(element => element is CatchAllPattern))
            {
                yield break;
            }
        }
    }
}