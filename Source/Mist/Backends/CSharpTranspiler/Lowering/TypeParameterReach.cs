using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class TypeParameterReach
{
    public static IReadOnlyList<TypeParameter> Collect(IMistType type)
    {
        var found = new List<TypeParameter>();
        Collect(type, found, new HashSet<IMistType>());
        return found;
    }

    private static void Collect(IMistType type, List<TypeParameter> found, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return;
        }

        switch(type)
        {
            case TypeParameter parameter:
                found.Add(parameter);
                break;

            case ConstructedType constructed:
                CollectAll(constructed.TypeArguments, found, seen);

                if(constructed.Enclosing is { } enclosing)
                {
                    Collect(enclosing, found, seen);
                }

                break;

            case UnionType union:
                CollectAll(union.Constituents, found, seen);
                break;

            case NamedTupleType named:
                CollectAll(named.ElementTypes, found, seen);
                break;

            case TupleType tuple:
                CollectAll(tuple.ElementTypes, found, seen);
                break;

            case FunctionType function:
                Collect(function.ReturnType, found, seen);
                CollectAll(function.ParameterTypes, found, seen);
                break;

            case LiftedType lifted:
                Collect(lifted.Type, found, seen);

                if(lifted.Value is TypePackValue pack)
                {
                    CollectAll(pack.Elements, found, seen);
                }

                break;

            case SpreadType spread:
                Collect(spread.Operand, found, seen);
                break;

            case RefinedType refined:
                Collect(refined.BaseType, found, seen);
                break;
        }
    }

    private static void CollectAll(IReadOnlyList<IMistType> types, List<TypeParameter> found, HashSet<IMistType> seen)
    {
        foreach(var type in types)
        {
            Collect(type, found, seen);
        }
    }
}