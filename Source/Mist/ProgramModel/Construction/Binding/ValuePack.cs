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
}