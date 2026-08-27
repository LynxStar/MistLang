using Mist.Common.Diagnostics;
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
    /// The slots that become type parameters on the emitted interface, in declaration order. That
    /// order is the whole agreement between the declaration and its implementors' base lists.
    /// </summary>
    public static IReadOnlyList<TypeSlot> Lifted(IMistType type)
    {
        if(Definition(type) is not AbstractType abstractType || ClrProjection.Projects(abstractType))
        {
            return [];
        }

        var lifted = new List<TypeSlot>(abstractType.TypeSlotsList.Count);
        foreach(var slot in abstractType.TypeSlotsList)
        {
            if(slot.TypeParametersList.Count == 0)
            {
                lifted.Add(slot);
            }
        }

        return lifted;
    }

    /// <summary>
    /// Every abstract <paramref name="type"/> reaches, whether by naming it in the base list or by
    /// conforming to it retroactively. Both spell the abstract into the emitted base list, so both
    /// have to carry the lift.
    /// </summary>
    public static IEnumerable<IMistType> Reached(MemberType type)
    {
        foreach(var abstractBase in type.BaseAbstractTypes)
        {
            yield return abstractBase;
        }

        foreach(var conformance in (type as ConcreteType)?.ConformancesList ?? [])
        {
            yield return conformance.AbstractType;
        }
    }

    /// <summary>
    /// The base-list entry for <paramref name="abstractRef"/> as reached from
    /// <paramref name="conformer"/>, with the filler for each lifted slot appended to whatever type
    /// arguments were already written. Null when the abstract lifts nothing, or when a filler is
    /// missing, leaving the entry to be spelled as it always was -- which
    /// <see cref="ReportUnfilled"/> has already refused by then.
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
            if(Filler(slot, conformer) is not { } filler)
            {
                return null;
            }

            arguments.Add(filler);
        }

        return new ConstructedType(Definition(abstractRef), arguments);
    }

    /// <summary>
    /// Reports every lifted slot no nested type fills. Split from <see cref="Base"/>, which two
    /// callers use, so the refusal happens once per declaration rather than once per spelling of
    /// its base list.
    /// </summary>
    public static void ReportUnfilled(MemberType type, DiagnosticBag diagnostics)
    {
        foreach(var abstractRef in Reached(type))
        {
            foreach(var slot in Lifted(abstractRef))
            {
                if(Filler(slot, type) is null)
                {
                    Report(BackendDiagnostics.UnfilledTypeSlot, type, diagnostics,
                        TypeDisplay.Mist(type),
                        MistType.StripArity(slot.Name),
                        TypeDisplay.Mist(Definition(abstractRef)));
                }
            }
        }
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
                    TypeDisplay.Mist(type), MistType.StripArity(slot.Name));
            }
        }
    }

    /// <summary>
    /// The nested type on <paramref name="conformer"/> that fills <paramref name="slot"/>, matched by
    /// name and arity. Inherited fillers count, so the base chain is walked.
    /// </summary>
    private static IMistType? Filler(TypeSlot slot, MemberType conformer)
    {
        var name = MistType.StripArity(slot.Name);

        foreach(var nested in conformer.NestedTypes)
        {
            if(nested is MemberType member
                && MistType.StripArity(member.Name) == name
                && member.TypeParametersList.Count == slot.TypeParametersList.Count)
            {
                return member;
            }
        }

        return conformer is ConcreteType { BaseType: MemberType inherited }
            ? Filler(slot, inherited)
            : null
            ;
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
        DiagnosticDescriptor descriptor,
        MemberType type,
        DiagnosticBag diagnostics,
        params object?[] arguments)
    {
        if(type.DeclarationNode is { } node)
        {
            diagnostics.ReportAtNode(descriptor, node, null, arguments);
            return;
        }

        diagnostics.Report(descriptor, default, "<unknown>", arguments);
    }

    public static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }
}