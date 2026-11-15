using Mist.ProgramAnalysis;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

internal sealed partial class SlotSensitivity
{
    internal static IEnumerable<MemberType> SourceDeclared(ProgramModelClass model)
    {
        return model.TypesByFQN.Values.OfType<MemberType>().Where(type => type.DeclarationNode is not null && !ClrProjection.Projects(type));
    }

    internal static IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> Closings(ProgramModelClass model, IReadOnlyList<object> roots)
    {
        foreach(var constructed in model.TypeInterner.ConstructedTypes)
        {
            if(constructed.Definition is IHasTypeParameters generic)
            {
                yield return (generic, constructed.TypeArguments);
            }
        }

        foreach(var (method, typeArguments) in model.GenericCalls.All())
        {
            yield return (method, typeArguments);
        }

        foreach(var root in roots)
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root))
            {
                if(SlotRoutes.ClosedThroughReceiver(node) is var (block, arguments))
                {
                    yield return (block, arguments);
                }
            }
        }
    }

    internal static List<ConstructedType> RoutedClosings(IMistType argument)
    {
        return [.. Constructed(argument).Where(closing => SlotRoutes.ValueRouted(closing.Definition).Count > 0)];
    }

    internal static IEnumerable<ConstructedType> Constructed(IMistType type)
    {
        var found = new List<ConstructedType>();
        Collect(type, found, new HashSet<IMistType>(ReferenceEqualityComparer.Instance));
        return found;
    }

    private static void Collect(IMistType type, List<ConstructedType> found, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return;
        }

        switch(type)
        {
            case ConstructedType constructed:
                found.Add(constructed);

                foreach(var argument in constructed.TypeArguments)
                {
                    Collect(argument, found, seen);
                }

                if(constructed.Enclosing is { } enclosing)
                {
                    Collect(enclosing, found, seen);
                }

                break;

            case FunctionType function:
                Collect(function.ReturnType, found, seen);

                foreach(var parameterType in function.ParameterTypes)
                {
                    Collect(parameterType, found, seen);
                }

                break;

            default:
                foreach(var part in Parts(type))
                {
                    Collect(part, found, seen);
                }

                break;
        }
    }
}