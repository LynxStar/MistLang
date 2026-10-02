using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A value bound at a position with a target type, wrapped in the implicit conversion Mist selects for it.
/// Recorded rather than left to the emitted C#, which resolves user-defined conversions by rules Mist does
/// not share: it refuses a signed parameter over an unsigned one as ambiguous, picks the most encompassing
/// result where Mist ties, and cannot apply a declared operator beside a lossless step it spells as a second
/// one.
/// </summary>
internal static class ImplicitConversions
{
    public static Expression At(Expression value, IMistType? target, TypeInterner interner)
    {
        if(target is null || value is SpreadExpression)
        {
            return value;
        }

        var called = CastOperatorSelection.Implicit(value.Type, target, interner, ConstantConversion.IntegerConstant(value));

        return called.Count == 0 ? value : new ImplicitConversionExpression(value.Node, value, target, called);
    }

    public static Expression DeclaredAt(Expression value, IMistType target, TypeInterner interner)
    {
        return At(value, target, interner) is ImplicitConversionExpression { Operator.Modifiers.IsIntrinsic: false } converted ? converted : value;
    }

    public static IReadOnlyList<Expression> AtParameters(
        IReadOnlyList<Expression> arguments, Method? method, IReadOnlyList<IMistType>? parameterTypes, TypeInterner interner)
    {
        if(parameterTypes is null)
        {
            return arguments;
        }

        if(method is null || ValuePack.FlattenedSlot(method.ParametersList) is not { } flattened)
        {
            return [.. arguments.Select((argument, i) => i < parameterTypes.Count ? At(argument, parameterTypes[i], interner) : argument)];
        }

        if(flattened >= parameterTypes.Count || ParamsCollection.ElementOf(parameterTypes[flattened]) is not { } element)
        {
            return arguments;
        }

        var parameters = method.ParametersList;
        var expanded = arguments.Count != parameters.Count || !ParamsCollection.IsHandedWhole(arguments[flattened].Type);

        return [.. arguments.Select((argument, i) =>
        {
            var slot = ValuePack.SlotOf(parameters, arguments.Count, i);

            return slot >= parameterTypes.Count ? argument : At(argument, slot == flattened && expanded ? element : parameterTypes[slot], interner);
        })];
    }
}