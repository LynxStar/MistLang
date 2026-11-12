using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramAnalysis;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using ProgramModelClass = Mist.ProgramModel.Model.ProgramModel;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// Which dependent parameters leave the C# type parameter list and carry their value in the instance or
/// the call, decided once over the whole program (runtime-dependent-values design, <em>Two routes</em>). A
/// parameter is value-routed when, to a fixed point, some closing fills it with a capture, a value-routed
/// parameter is forwarded into it, its slot is read through <c>unknown</c>, or another dependent
/// parameter of its declaration is value-routed. Whole-program rather than per declaration because a
/// declaration's route is fixed by its uses, and a closing written in one file carries a value a type
/// declared in another must hold. An abstract's method and each method answering it take one route,
/// because C# matches an implementation to its interface member by signature and a hidden parameter
/// on one side only would leave the member unimplemented. An extension block's own parameters are
/// closed on a copy of the method at lookup rather than recorded with the call's type arguments, so
/// each call is read for them. Array and the spans are left out: <see cref="LengthErasure"/> drops
/// their slot and the CLR object holds the value already.
/// </summary>
internal sealed class SlotRoutes : IDisposable
{
    [ThreadStatic]
    private static SlotRoutes? current;

    private readonly HashSet<TypeParameter> valueRouted;

    private SlotRoutes(HashSet<TypeParameter> valueRouted)
    {
        this.valueRouted = valueRouted;
    }

    public static IDisposable Enter(ProgramModelClass model)
    {
        return Enter(Discover(model));
    }

    public static IDisposable Enter(IEnumerable<TypeParameter> routed)
    {
        current = new SlotRoutes(new HashSet<TypeParameter>(routed, ReferenceEqualityComparer.Instance));
        return current;
    }

    public void Dispose()
    {
        current = null;
    }

    public static bool IsValueRouted(TypeParameter parameter)
    {
        return current?.valueRouted.Contains(parameter) == true;
    }

    public static IReadOnlyList<TypeParameter> ValueRouted(IMistType? type)
    {
        var definition = type is ConstructedType constructed ? constructed.Definition : type;

        return definition is IHasTypeParameters generic ? ValueRouted(generic.TypeParametersList) : [];
    }

    public static IReadOnlyList<TypeParameter> ValueRouted(IReadOnlyList<TypeParameter> parameters)
    {
        return current is null ? [] : [.. parameters.Where(IsValueRouted)];
    }

    public static string Hidden(TypeParameter parameter)
    {
        return $"__{parameter.Name}";
    }

    public static string Keyword(TypeParameter parameter)
    {
        return parameter.Domain is { } domain ? DependentCarrierNames.Keyword(domain) ?? TypeNameEmitter.Emit(domain) : "int";
    }

    public static IEnumerable<string> HiddenParameters(IEnumerable<TypeParameter> parameters)
    {
        return parameters.Select(parameter => $"{Keyword(parameter)} {Hidden(parameter)}");
    }

    public static IEnumerable<string> HiddenNames(IEnumerable<TypeParameter> parameters)
    {
        return parameters.Select(Hidden);
    }

    public static ConstructedType? ClosingFrom(IMistType? enclosing, MemberType declaring)
    {
        var path = new List<ConstructedType>();

        for(var type = enclosing is ConstructedType constructed ? constructed.Definition : enclosing; type is not null;)
        {
            if(ReferenceEquals(type, declaring))
            {
                return TypeSubstitution.ForPath(receiver: null, path, interner: null)
                    .Apply(new ConstructedType(declaring, declaring.TypeParametersList), interner: null) as ConstructedType;
            }

            if(type is not ConcreteType { BaseType: { } written })
            {
                return null;
            }

            if(written is ConstructedType step)
            {
                path.Add(step);
            }

            type = written is ConstructedType constructedBase ? constructedBase.Definition : written;
        }

        return null;
    }

    public static HashSet<TypeParameter> Discover(ProgramModelClass model)
    {
        var closings = new List<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)>();

        foreach(var constructed in model.TypeInterner.ConstructedTypes)
        {
            if(constructed.Definition is IHasTypeParameters generic)
            {
                closings.Add((generic, constructed.TypeArguments));
            }
        }

        foreach(var (method, typeArguments) in model.GenericCalls.All())
        {
            closings.Add((method, typeArguments));
        }

        var reads = new List<TypeParameter>();
        var inferred = new List<(IHasTypeParameters Definition, int Ordinal)>();

        foreach(var root in SourceBodies.Of(model))
        {
            foreach(var node in BodyWalker.DescendantsAndSelf(root.Root))
            {
                if(node is MemberAccessExpression access && ReadThroughUnknown(access) is { } slot)
                {
                    reads.Add(slot);
                }

                if(node is not InvocationExpression invocation || ClosedThroughReceiver(invocation) is not var (block, arguments))
                {
                    continue;
                }

                closings.Add((block, arguments));

                for(var i = 0; i < arguments.Count; i++)
                {
                    if(arguments[i] is not (UnknownType or UnknownValueType))
                    {
                        continue;
                    }

                    inferred.Add((block, i));

                    if(ReceiverSlot(block, block.TypeParametersList[i]) is var (_, carried))
                    {
                        reads.Add(carried);
                    }
                }
            }
        }

        return Discover(closings, reads, Declarations(model), Answers(model), inferred);
    }

    public static (ExtensionBlock Block, IReadOnlyList<IMistType> Arguments)? ClosedThroughReceiver(InvocationExpression invocation)
    {
        return invocation.Method is { ClosedBlock: { DeclarationNode: not null } block } method
            ? (block, block.TypeParametersList.ConvertAll(parameter => method.BlockClosing.Apply(parameter, interner: null)))
            : null;
    }

    public static (ConstructedType Receiver, TypeParameter Slot)? ReceiverSlot(ExtensionBlock block, TypeParameter parameter)
    {
        if(block.ResolvedReceiverType is not { } receiverType
            || TypeNameEmitter.Closed(receiverType) is not ConstructedType receiver
            || receiver.Definition is not IHasTypeParameters generic)
        {
            return null;
        }

        for(var i = 0; i < receiver.TypeArguments.Count && i < generic.TypeParametersList.Count; i++)
        {
            if(ReferenceEquals(receiver.TypeArguments[i], parameter))
            {
                return (receiver, generic.TypeParametersList[i]);
            }
        }

        return null;
    }

    public static TypeParameter? ReadThroughUnknown(MemberAccessExpression access)
    {
        return InstanceSlotReads.Through(access) is var (closing, slot)
            && slot.Ordinal < closing.TypeArguments.Count
            && closing.TypeArguments[slot.Ordinal] is UnknownType or UnknownValueType
                ? slot
                : null;
    }

    public static HashSet<TypeParameter> Discover(
        IEnumerable<(IHasTypeParameters Definition, IReadOnlyList<IMistType> Arguments)> closings,
        IEnumerable<TypeParameter> readThroughUnknown,
        IEnumerable<IHasTypeParameters> declarations,
        IEnumerable<(Method Slot, Method Answer)>? answers = null,
        IEnumerable<(IHasTypeParameters Definition, int Ordinal)>? inferredFromUnknown = null)
    {
        var routed = new HashSet<TypeParameter>(ReferenceEqualityComparer.Instance);
        var fills = closings.ToList();
        var answered = answers?.ToList() ?? [];
        var siblings = declarations.Concat(fills.Select(fill => fill.Definition)).Distinct(ReferenceEqualityComparer.Instance).Cast<IHasTypeParameters>().ToList();

        bool route(IHasTypeParameters definition, int ordinal)
        {
            return ordinal < definition.TypeParametersList.Count && Routable(definition, definition.TypeParametersList[ordinal]) && routed.Add(definition.TypeParametersList[ordinal]);
        }

        foreach(var (definition, arguments) in fills)
        {
            for(var i = 0; i < arguments.Count; i++)
            {
                if(arguments[i] is CapturedValue)
                {
                    _ = route(definition, i);
                }
            }
        }

        foreach(var (definition, ordinal) in inferredFromUnknown ?? [])
        {
            _ = route(definition, ordinal);
        }

        foreach(var slot in readThroughUnknown)
        {
            if(Declaring(slot) is { } definition)
            {
                _ = route(definition, slot.Ordinal);
            }
        }

        var changed = true;

        while(changed)
        {
            changed = false;

            foreach(var (definition, arguments) in fills)
            {
                for(var i = 0; i < arguments.Count; i++)
                {
                    if(arguments[i] is TypeParameter forwarded && routed.Contains(forwarded))
                    {
                        changed |= route(definition, i);
                    }
                }
            }

            foreach(var definition in siblings)
            {
                if(definition.TypeParametersList.Exists(routed.Contains))
                {
                    for(var i = 0; i < definition.TypeParametersList.Count; i++)
                    {
                        changed |= route(definition, i);
                    }
                }
            }

            foreach(var (slot, answer) in answered)
            {
                for(var i = 0; i < slot.TypeParametersList.Count && i < answer.TypeParametersList.Count; i++)
                {
                    if(routed.Contains(slot.TypeParametersList[i]))
                    {
                        changed |= route(answer, i);
                    }

                    if(routed.Contains(answer.TypeParametersList[i]))
                    {
                        changed |= route(slot, i);
                    }
                }
            }
        }

        return routed;
    }

    public static IEnumerable<(Method Slot, Method Answer)> Answers(ProgramModelClass model)
    {
        var interner = model.TypeInterner;

        foreach(var type in model.TypesByFQN.Values)
        {
            if(type is not ConcreteType { DeclarationNode: not null } concrete)
            {
                continue;
            }

            List<ConformanceAnalyzer.Filler>? fillers = null;
            var reached = ConformanceAnalyzer.Reached(concrete.BaseAbstractTypes, interner, concrete)
                .Select(each => (each.Abstract, each.Substitution, Mappings: (ConformanceDeclaration?)null))
                .Concat(concrete.ConformancesList.SelectMany(conformance => ConformanceAnalyzer.Reached([conformance.AbstractType], interner, concrete)
                    .Select(each => (each.Abstract, each.Substitution, Mappings: (ConformanceDeclaration?)conformance))));

            foreach(var (abstractType, substitution, mappings) in reached)
            {
                foreach(var slot in abstractType.MethodsList)
                {
                    if(slot.Modifiers.IsStatic || !slot.TypeParametersList.Exists(parameter => parameter.IsDependent))
                    {
                        continue;
                    }

                    fillers ??= ConformanceAnalyzer.Fillers(concrete).ToList();

                    if(ConformanceAnswers.Method(slot, substitution, fillers, mappings, interner) is { } answer)
                    {
                        yield return (slot, answer.Member);
                    }
                }
            }
        }
    }

    private static bool Routable(IHasTypeParameters definition, TypeParameter parameter)
    {
        if(parameter is not { IsDependent: true, IsPack: false })
        {
            return false;
        }

        return definition switch
        {
            MemberType member => !member.Modifiers.IsIntrinsic && !ClrProjection.Projects(member)
                && !LengthErasure.IsLengthIndexed(new ConstructedType(member, member.TypeParametersList)),
            Method method => method.Modifiers is { IsIntrinsic: false, IsClrProjected: false },
            _ => true,
        };
    }

    private static IHasTypeParameters? Declaring(TypeParameter parameter)
    {
        return parameter.DeclaringMethod ?? parameter.DeclaringType as IHasTypeParameters;
    }

    private static IEnumerable<IHasTypeParameters> Declarations(ProgramModelClass model)
    {
        foreach(var type in model.TypesByFQN.Values)
        {
            if(type is IHasTypeParameters generic)
            {
                yield return generic;
            }

            if(type is MemberType member)
            {
                foreach(var method in member.MethodsList)
                {
                    yield return method;
                }
            }
        }

        foreach(var block in SourceTypes.ExtensionsOf(model))
        {
            yield return block;

            foreach(var method in block.MethodsList)
            {
                yield return method;
            }
        }
    }
}