using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// How an associated type slot is spelled in C#, which has no abstract nested type. A slot with no
/// type parameters of its own LIFTS to a type parameter on the emitted interface, and the
/// implementor's base list passes the nested type filling it. A PARAMETERIZED slot cannot: it would
/// need a higher-kinded parameter, so it ERASES to its bound instead, closed by whatever the use
/// site applied it to.
/// <para>
/// A lifted parameter is named for its slot rather than being prefixed, which is what lets a member
/// signature go on emitting a slot-typed member through <see cref="Emitter.TypeNameEmitter"/>'s
/// ordinary type-parameter path with no substitution anywhere. Renaming would instead put a
/// rewrite on every position a type can occupy, and the name is already the one Mist resolves --
/// inside the abstract, the slot shadows an outer type of the same name, so a parameter carrying
/// the slot's name reproduces the scoping the source already has rather than inventing one.
/// </para>
/// </summary>
internal static class SlotLift
{
    /// <summary>
    /// The slots that become type parameters on the emitted interface: the abstract's own in
    /// declaration order, then those of every abstract it inherits in reach order. That order is the
    /// whole agreement between the declaration and its implementors' base lists. An inherited slot
    /// is lifted again because C# gives a derived interface no parameter of its base's; it is named
    /// once because <see cref="TypeSlots.Filler"/> matches a filler by name, so two same-named slots
    /// from different contracts can only ever be filled by one nested type, and an own slot shadows
    /// an inherited one as it does in Mist's scope.
    /// </summary>
    public static IReadOnlyList<TypeSlot> Lifted(IMistType type)
    {
        if(Definition(type) is not AbstractType abstractType || ClrProjection.Projects(abstractType))
        {
            return [];
        }

        var lifted = new List<TypeSlot>();
        var names = new HashSet<string>(StringComparer.Ordinal);
        Collect(abstractType, lifted, names);

        foreach(var (_, inherited, _) in AbstractInheritance.Reached(abstractType.BaseAbstractTypes, interner: null))
        {
            if(!ClrProjection.Projects(inherited))
            {
                Collect(inherited, lifted, names);
            }
        }

        return lifted;
    }

    private static void Collect(AbstractType abstractType, List<TypeSlot> lifted, HashSet<string> names)
    {
        foreach(var slot in abstractType.TypeSlotsList)
        {
            if(slot.TypeParametersList.Count == 0 && names.Add(MistType.StripArity(slot.Name)))
            {
                lifted.Add(slot);
            }
        }
    }

    public static IEnumerable<IMistType> Reached(MemberType type)
    {
        return TypeSlots.Reached(type);
    }

    /// <summary>
    /// The slots a method's type parameters reach through their constraints, each once, in the
    /// order the parameters and their constraints declare them. A method generic over a lifting
    /// abstract declares one extra type parameter per slot, named for the slot as the interface's
    /// is, so a member typed with the slot inside the body spells the parameter untouched; the
    /// constraint names it, <c>where T : IProducer&lt;Output&gt;</c>, and a call supplies the filler.
    /// </summary>
    public static IReadOnlyList<TypeSlot> Synthesized(IReadOnlyList<TypeParameter> typeParameters)
    {
        return Through(typeParameters).Select(pair => pair.Slot).ToList();
    }

    public static IMistType Constraint(IMistType constraintType)
    {
        var lifted = Lifted(constraintType);

        if(lifted.Count == 0)
        {
            return constraintType;
        }

        var arguments = new List<IMistType>();

        if(constraintType is ConstructedType constructed)
        {
            arguments.AddRange(constructed.TypeArguments);
        }

        arguments.AddRange(lifted);
        return new ConstructedType(Definition(constraintType), arguments);
    }

    /// <summary>
    /// The type arguments a call adds for the slots its callee synthesized: the filler declared on
    /// the concrete argument, or the slot itself when the argument is a type parameter, since the
    /// caller then synthesized the same parameter under the same name.
    /// </summary>
    public static IReadOnlyList<IMistType> Fillers(
        IReadOnlyList<IMistType> typeArguments, IReadOnlyList<TypeParameter> typeParameters)
    {
        var fillers = new List<IMistType>();

        foreach(var (ordinal, slot) in Through(typeParameters))
        {
            var argument = ordinal < typeArguments.Count ? typeArguments[ordinal] : null;
            var conformer = argument is ConstructedType constructed ? constructed.Definition : argument;

            fillers.Add(conformer is MemberType member && argument is not TypeParameter
                ? Filler(slot, member) ?? slot
                : slot);
        }

        return fillers;
    }

    private static IEnumerable<(int Ordinal, TypeSlot Slot)> Through(IReadOnlyList<TypeParameter> typeParameters)
    {
        var seen = new HashSet<TypeSlot>(ReferenceEqualityComparer.Instance);

        for(var ordinal = 0; ordinal < typeParameters.Count; ordinal++)
        {
            foreach(var constraint in typeParameters[ordinal].ConstraintsList.OfType<TypeConstraint>())
            {
                foreach(var slot in Lifted(constraint.ConstraintType))
                {
                    if(seen.Add(slot))
                    {
                        yield return (ordinal, slot);
                    }
                }
            }
        }
    }

    /// <summary>
    /// The base-list entry for <paramref name="abstractRef"/> as reached from
    /// <paramref name="conformer"/>, with the argument for each lifted slot appended to whatever type
    /// arguments were already written: the filler a concrete conformer declares, or the same-named
    /// parameter a derived abstract lifted for itself. Null when the abstract lifts nothing, or when
    /// an argument is missing, leaving the entry to be spelled as it always was -- which
    /// the conformance rule has already refused by then.
    /// </summary>
    public static IMistType? Base(IMistType abstractRef, MemberType conformer)
    {
        var lifted = Lifted(abstractRef);
        if(lifted.Count == 0)
        {
            return null;
        }

        var arguments = new List<IMistType>();
        if(abstractRef is ConstructedType constructed)
        {
            arguments.AddRange(constructed.TypeArguments);
        }

        foreach(var slot in lifted)
        {
            if(Argument(slot, conformer) is not { } argument)
            {
                return null;
            }

            arguments.Add(argument);
        }

        return new ConstructedType(Definition(abstractRef), arguments);
    }

    private static IMistType? Argument(TypeSlot slot, MemberType conformer)
    {
        if(conformer is not AbstractType)
        {
            return Filler(slot, conformer);
        }

        var name = MistType.StripArity(slot.Name);
        return Lifted(conformer).FirstOrDefault(own => MistType.StripArity(own.Name) == name);
    }

    /// <summary>
    /// The bound a parameterized slot erases to, with the slot's own parameters closed by the
    /// arguments the use site applied. Null when <paramref name="type"/> is not such a slot, or
    /// declares no bound to erase to.
    /// </summary>
    public static IMistType? Erased(IMistType type)
    {
        if(type is not ConstructedType { Definition: TypeSlot slot } constructed
            || slot.TypeParametersList.Count == 0)
        {
            return null;
        }

        if(Bound(slot) is not { } bound)
        {
            return null;
        }

        return TypeSubstitution
            .ForParameters(slot.TypeParametersList, constructed.TypeArguments)
            .Apply(bound, interner: null);
    }

    /// <summary>
    /// Reports every parameterized slot that has no bound to erase to. Separate from
    /// <see cref="Erased"/> so the report happens once per declaration rather than once per use,
    /// and so a slot used nowhere is still refused.
    /// </summary>
    public static void ReportUnboundedParameterized(MemberType type, DiagnosticBag diagnostics)
    {
        if(type is not AbstractType abstractType)
        {
            return;
        }

        foreach(var slot in abstractType.TypeSlotsList)
        {
            if(slot.TypeParametersList.Count > 0 && Bound(slot) is null)
            {
                Report(BackendDiagnostics.UnboundedParameterizedTypeSlot, type, diagnostics,
                    DiagnosticSnapshot.Type(type), new NameValue(MistType.StripArity(slot.Name)));
            }
        }
    }

    private static IMistType? Filler(TypeSlot slot, MemberType conformer)
    {
        return TypeSlots.Filler(slot, conformer);
    }

    private static IMistType? Bound(TypeSlot slot)
    {
        foreach(var constraint in slot.ConstraintsList)
        {
            if(constraint is TypeConstraint typeConstraint
                && Definition(typeConstraint.ConstraintType) is AbstractType)
            {
                return typeConstraint.ConstraintType;
            }
        }

        return null;
    }

    private static void Report(
        MistDiagnostic descriptor,
        MemberType type,
        DiagnosticBag diagnostics,
        params SlotValue[] arguments)
    {
        if(type.DeclarationNode is { } node)
        {
            diagnostics.ReportAtNodeTyped(descriptor, node, null, arguments);
            return;
        }

        diagnostics.ReportTyped(descriptor, new ExternalLocation(type.Assembly.Name, type.FullyQualifiedName), arguments);
    }

    public static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }
}