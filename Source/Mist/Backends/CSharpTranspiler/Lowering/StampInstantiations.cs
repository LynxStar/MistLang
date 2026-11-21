using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramAnalysis;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The closed types each stampable type parameter is bound to anywhere in the program, read off its
/// constructions and generic calls and carried through forwarding, so a generic body that passes a Box
/// of its own parameter on binds the callee to a Box of whatever its caller chose, a construction no
/// source names. A method answering an abstract slot is bound to whatever the slot is, since a call
/// through the slot runs its body. Only stampable parameters are followed, because only a stampable
/// argument reaches one.
/// </summary>
internal static class StampInstantiations
{
    private const int Rounds = 16;

    private const int Limit = 256;

    public static IReadOnlyList<IMistType> Close(ProgramModelClass model, IEnumerable<IMistType> elementTypes)
    {
        var bindings = Bindings(model);
        var closed = new SortedDictionary<string, IMistType>(StringComparer.Ordinal);

        foreach(var instance in elementTypes.SelectMany(type => Instantiate(type, bindings)))
        {
            _ = closed.TryAdd(TypeNameEmitter.Emit(instance), instance);
        }

        return [.. closed.Values];
    }

    public static IReadOnlyList<IMistType> Candidates(ProgramModelClass model)
    {
        var parameters = Edges(model).Select(edge => (IMistType)edge.Parameter);
        var open = model.TypeInterner.ConstructedTypes.Where(constructed => TypeParameterReach.Collect(constructed).Count > 0);
        return Close(model, parameters.Concat(open));
    }

    private static Dictionary<TypeParameter, Dictionary<string, IMistType>> Bindings(ProgramModelClass model)
    {
        var edges = Edges(model).ToList();
        var bindings = new Dictionary<TypeParameter, Dictionary<string, IMistType>>(ReferenceEqualityComparer.Instance);

        for(var round = 0; round < Rounds; round++)
        {
            var grew = false;

            foreach(var (parameter, argument) in edges)
            {
                foreach(var instance in Instantiate(argument, bindings).ToList())
                {
                    if(!bindings.TryGetValue(parameter, out var bound))
                    {
                        bound = new Dictionary<string, IMistType>(StringComparer.Ordinal);
                        bindings[parameter] = bound;
                    }

                    grew |= bound.Count < Limit && bound.TryAdd(TypeNameEmitter.Emit(instance), instance);
                }
            }

            if(!grew)
            {
                break;
            }
        }

        return bindings;
    }

    private static IEnumerable<(TypeParameter Parameter, IMistType Argument)> Edges(ProgramModelClass model)
    {
        foreach(var constructed in model.TypeInterner.ConstructedTypes)
        {
            if(constructed.Definition is IHasTypeParameters generic)
            {
                foreach(var edge in Stamped(generic.TypeParametersList, constructed.TypeArguments))
                {
                    yield return edge;
                }
            }
        }

        foreach(var (method, typeArguments) in model.GenericCalls.All())
        {
            foreach(var edge in Stamped(method.TypeParametersList, typeArguments))
            {
                yield return edge;
            }
        }

        foreach(var edge in Answers(model))
        {
            yield return edge;
        }
    }

    private static IEnumerable<(TypeParameter, IMistType)> Answers(ProgramModelClass model)
    {
        var types = SourceTypes.Of(model).ToList();
        var slotted = types.OfType<AbstractType>()
            .Where(abstractType => abstractType.MethodsList.Exists(slot => Stamped(slot.TypeParametersList, slot.TypeParametersList).Any()))
            .ToHashSet(ReferenceEqualityComparer.Instance);

        if(slotted.Count == 0)
        {
            return [];
        }

        return types.OfType<ConcreteType>()
            .SelectMany(concrete => ContextCalls.Answers(concrete, model.TypeInterner))
            .Where(answer => slotted.Contains(answer.Abstract))
            .SelectMany(answer => Stamped(answer.Answer.Member.TypeParametersList, answer.Slot.TypeParametersList));
    }

    private static IEnumerable<(TypeParameter, IMistType)> Stamped(IReadOnlyList<TypeParameter> parameters, IReadOnlyList<IMistType> arguments)
    {
        for(var i = 0; i < parameters.Count && i < arguments.Count; i++)
        {
            if(parameters[i] is { IsPack: false, IsDependent: false } parameter
                && parameter.ConstraintsList.Exists(constraint => constraint.Kind == TypeParameterConstraintKind.Stampable))
            {
                yield return (parameter, arguments[i]);
            }
        }
    }

    private static IEnumerable<IMistType> Instantiate(IMistType type, Dictionary<TypeParameter, Dictionary<string, IMistType>> bindings)
    {
        IEnumerable<TypeSubstitution> substitutions = [TypeSubstitution.Empty];

        foreach(var parameter in TypeParameterReach.Collect(type).Distinct())
        {
            if(!bindings.TryGetValue(parameter, out var bound))
            {
                return [];
            }

            substitutions = substitutions.SelectMany(substitution => bound.Values.Select(argument => substitution.With(parameter, argument)));
        }

        return substitutions.Take(Limit).Select(substitution => substitution.Apply(type, interner: null));
    }
}