using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The operand of a cast, converted to the parameter type of the conversion function the cast calls when
/// it is not already that type. Mist matches the operand as an argument, so it may reach the parameter
/// through a conversion of its own -- another enum, a union carrier, a declared implicit operator. A C#
/// cast applies at most one user-defined conversion, and each of those is one, so the operand must reach
/// the parameter in a cast of its own before the called operator can take it: <c>((Feet)(Meters)(reading))</c>.
/// That limit is C#'s, not Mist's, which is why it is lowered here rather than refused. An implicit
/// conversion spells both ends as casts for the same reason, and because C# would otherwise choose the
/// operator again by rules of its own.
/// </summary>
internal static class CastLowering
{
    public static string Argument(CastExpression cast, string operand)
    {
        return cast.ParameterType is { } parameter
            ? Step(cast.Operand.Type, parameter, operand)
            : operand
            ;
    }

    public static string Implicit(ImplicitConversionExpression conversion, string operand)
    {
        return conversion.ParameterType is { } parameter
            ? $"(({TypeNameEmitter.Emit(conversion.Type)})({Step(conversion.Operand.Type, parameter, operand)}))"
            : operand
            ;
    }

    private static string Step(IMistType from, IMistType parameter, string operand)
    {
        return ConversionRelation.IsIdentity(from, parameter)
            ? operand
            : $"({TypeNameEmitter.Emit(parameter)})({operand})"
            ;
    }
}