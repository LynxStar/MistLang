using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Whether a type mentions an <see cref="AnonymousType"/> anywhere inside it, which is what makes the
/// whole type unspellable in C# -- an array of anonymous values has no more of a name than one value
/// does. Asked at the declarations that would have to write the name, so they can let C# infer it
/// instead.
/// </summary>
internal static class AnonymousReach
{
    public static bool Mentions(IMistType type)
    {
        return Mentions(type, new HashSet<IMistType>());
    }

    private static bool Mentions(IMistType type, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return false;
        }

        return type switch
        {
            AnonymousType => true,
            ConstructedType constructed => Any(constructed.TypeArguments, seen),
            UnionType union => Any(union.Constituents, seen),
            NamedTupleType named => Any(named.ElementTypes, seen),
            TupleType tuple => Any(tuple.ElementTypes, seen),
            FunctionType function => Mentions(function.ReturnType, seen)
                || Any(function.ParameterTypes, seen),
            LiftedType lifted => Mentions(lifted.Type, seen),
            RefinedType refined => Mentions(refined.BaseType, seen),
            _ => false,
        };
    }

    private static bool Any(IReadOnlyList<IMistType> types, HashSet<IMistType> seen)
    {
        foreach(var type in types)
        {
            if(Mentions(type, seen))
            {
                return true;
            }
        }

        return false;
    }
}