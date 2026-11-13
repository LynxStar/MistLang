using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal static class TypeParameterReach
{
    public static IReadOnlyList<TypeParameter> Collect(IMistType type)
    {
        var found = new List<TypeParameter>();
        Collect(type, found, new HashSet<IMistType>(), carriedOnly: false);
        return found;
    }

    /// <summary>
    /// The parameters a C# spelling of the type still carries: a value-routed parameter leaves the C#
    /// type parameter list, and so does whatever stands in a value-routed slot of a closing, so neither
    /// makes the spelling open or distinguishes one instantiation's spelling from another's.
    /// </summary>
    public static IReadOnlyList<TypeParameter> Carried(IMistType type)
    {
        var found = new List<TypeParameter>();
        Collect(type, found, new HashSet<IMistType>(), carriedOnly: true);
        return found;
    }

    private static void Collect(IMistType type, List<TypeParameter> found, HashSet<IMistType> seen, bool carriedOnly)
    {
        if(!seen.Add(type))
        {
            return;
        }

        switch(type)
        {
            case TypeParameter parameter:
                if(!carriedOnly || !SlotRoutes.IsValueRouted(parameter))
                {
                    found.Add(parameter);
                }

                break;

            case ConstructedType constructed:
                for(var ordinal = 0; ordinal < constructed.TypeArguments.Count; ordinal++)
                {
                    if(!carriedOnly || SlotRoutes.ValueRoutedSlot(constructed.Definition, ordinal) is null)
                    {
                        Collect(constructed.TypeArguments[ordinal], found, seen, carriedOnly);
                    }
                }

                if(constructed.Enclosing is { } enclosing)
                {
                    Collect(enclosing, found, seen, carriedOnly);
                }

                break;

            case UnionType union:
                CollectAll(union.Constituents, found, seen, carriedOnly);
                break;

            case NamedTupleType named:
                CollectAll(named.ElementTypes, found, seen, carriedOnly);
                break;

            case TupleType tuple:
                CollectAll(tuple.ElementTypes, found, seen, carriedOnly);
                break;

            case FunctionType function:
                Collect(function.ReturnType, found, seen, carriedOnly);
                CollectAll(function.ParameterTypes, found, seen, carriedOnly);
                break;

            case LiftedType lifted:
                Collect(lifted.Type, found, seen, carriedOnly);

                if(lifted.Value is TypePackValue pack)
                {
                    CollectAll(pack.Elements, found, seen, carriedOnly);
                }

                break;

            case SpreadType spread:
                Collect(spread.Operand, found, seen, carriedOnly);
                break;

            case RefinedType refined:
                Collect(refined.BaseType, found, seen, carriedOnly);
                break;
        }
    }

    private static void CollectAll(IReadOnlyList<IMistType> types, List<TypeParameter> found, HashSet<IMistType> seen, bool carriedOnly)
    {
        foreach(var type in types)
        {
            Collect(type, found, seen, carriedOnly);
        }
    }
}