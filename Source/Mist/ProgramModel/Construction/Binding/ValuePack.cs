using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// The value side of a pack: the one parameter slot a flat argument list collapses into.
/// <para>
/// The same division as the type side, one level down. A method's fixed parameters bind inward from
/// both ends and the flattened one takes the middle, so the arity a call must match stops being a
/// number and becomes a floor.
/// </para>
/// </summary>
internal static class ValuePack
{
    /// <summary>
    /// The index of the parameter a flat argument list collapses into, or null when every parameter
    /// takes exactly one argument.
    /// </summary>
    public static int? FlattenedSlot(IReadOnlyList<Parameter> parameters)
    {
        for(var i = 0; i < parameters.Count; i++)
        {
            if(parameters[i].IsFlattened)
            {
                return i;
            }
        }

        return null;
    }

    /// <summary>
    /// Whether this many arguments can fill these parameters. Exactly one each, unless one of them
    /// absorbs -- in which case the fixed ones are a floor and anything above it is the pack's.
    /// </summary>
    public static bool AcceptsCount(IReadOnlyList<Parameter> parameters, int argumentCount)
    {
        return FlattenedSlot(parameters) is null
            ? parameters.Count == argumentCount
            : argumentCount >= parameters.Count - 1;
    }

    /// <summary>
    /// The pack a value-pack parameter ranges over, or null for the homogeneous <c>params T[]</c>
    /// form, which has an array to collect into rather than a pack to bind.
    /// </summary>
    public static TypeParameter? PackParameterOf(Parameter parameter)
    {
        return parameter is { IsFlattened: true, Type: TupleType { ElementTypes: [SpreadType spread] } }
            && spread.Operand is TypeParameter { IsPack: true } pack
                ? pack
                : null;
    }

    public static IMistType? ElementOf(Parameter parameter)
    {
        return parameter.IsFlattened ? Indexability.ArrayElement(parameter.Type) : null;
    }

    /// <summary>
    /// The index of the parameter the argument at <paramref name="index"/> fills when this many
    /// arguments are passed: its own position before the flattened slot, the flattened slot through
    /// the middle, and the trailing fixed ones counted from the end.
    /// </summary>
    public static int SlotOf(IReadOnlyList<Parameter> parameters, int argumentCount, int index)
    {
        if(FlattenedSlot(parameters) is not { } flattened || index < flattened)
        {
            return index;
        }

        var trailing = parameters.Count - flattened - 1;

        return index >= argumentCount - trailing ? parameters.Count - (argumentCount - index) : flattened;
    }

    /// <summary>
    /// The argument types the flattened slot absorbs, in order.
    /// </summary>
    public static IReadOnlyList<IMistType> Absorbed(
        IReadOnlyList<Parameter> parameters, IReadOnlyList<IMistType> argumentTypes, int flattened)
    {
        var trailing = parameters.Count - flattened - 1;
        var count = argumentTypes.Count - flattened - trailing;

        return count <= 0 ? [] : [.. argumentTypes.Skip(flattened).Take(count)];
    }

    /// <summary>
    /// The parameters a signature has once <paramref name="substitution"/> binds its value pack, which
    /// is what an implementer of the signature writes (variadics Requirement 22.2): one parameter per
    /// element the pack holds, and a value pack of its own for an element that is still a spread. So
    /// <c>params TArgs args</c> is two parameters under <c>[int, int]</c>, none under <c>[]</c>, and
    /// the one pack it was under <c>[TRest...]</c>. Every other parameter is substituted in place.
    /// </summary>
    public static IReadOnlyList<Parameter> Expand(
        IReadOnlyList<Parameter> parameters, TypeSubstitution substitution, TypeInterner? interner)
    {
        var expanded = new List<Parameter>(parameters.Count);

        foreach(var parameter in parameters)
        {
            if(PackParameterOf(parameter) is not { } pack
                || substitution.Apply(pack, interner) is not LiftedType { Value: TypePackValue bound })
            {
                expanded.Add(Retyped(parameter, parameter.Name, substitution.Apply(parameter.Type, interner), parameter.IsFlattened));
                continue;
            }

            for(var i = 0; i < bound.Elements.Length; i++)
            {
                var element = bound.Elements[i];
                var name = bound.Elements.Length == 1 ? parameter.Name : $"{parameter.Name}{i + 1}";

                expanded.Add(element is SpreadType
                    ? Retyped(parameter, name, interner?.GetOrCreateTupleType([element]) ?? new TupleType([element]), isFlattened: true)
                    : Retyped(parameter, name, element, isFlattened: false));
            }
        }

        return expanded;
    }

    private static Parameter Retyped(Parameter parameter, string name, IMistType type, bool isFlattened)
    {
        return ReferenceEquals(type, parameter.Type) && name == parameter.Name
            ? parameter
            : new Parameter(name, type, parameter.Modifier) { IsFlattened = isFlattened };
    }
}