using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A use of a conversion <see cref="OperatorMethods"/> emits as a method. A cast's own capture is the hidden
/// argument; the operand is still evaluated before it, as a cast through a C# operator evaluates its operand before
/// its captures, so an operand that runs code is bound first when a capture runs code too. An implicit conversion's
/// target was written by another node, so the value it reads is the one that node captured, kept in a local, and not
/// the variable's value when the conversion runs.
/// </summary>
internal static partial class BodyEmitter
{
    private static string ConversionCall(CastExpression cast, DiagnosticBag diagnostics)
    {
        var conversion = cast.Operator!;
        var owner = OperatorMethods.Owner(conversion, cast.Type, cast.OperatorClosing);
        var unconsumed = cast.Captures.Where(capture => Evaluates(capture) && !ConsumedByClosing(owner, capture)).ToList();
        var consumed = cast.Captures.Any(capture => Evaluates(capture) && ConsumedByClosing(owner, capture));

        return Evaluating(unconsumed, cast, cast.Operand, () =>
        {
            if(consumed && cast.Operand is not LiteralExpression && !IsPlainRead(cast.Operand) && boundParts?.ContainsKey(cast.Operand) != true)
            {
                var name = TemporaryName();
                var call = OperatorMethods.Call(conversion, owner, [CastLowering.Argument(cast, name)], diagnostics);

                return $"({RenderAt(cast.Operand, SwitchPrecedence + 1, diagnostics)} switch {{ var {name} => {call} }})";
            }

            return OperatorMethods.Call(conversion, owner, [CastLowering.Argument(cast, Render(cast.Operand, diagnostics))], diagnostics);
        }, diagnostics);
    }

    private static IEnumerable<SlotCapture> ConvertedCaptures(ImplicitConversionExpression conversion)
    {
        return conversion.Operator is { } called && OperatorMethods.Applies(called)
            ? ClosingCaptures(OperatorMethods.Owner(called, conversion.Type, conversion.OperatorClosing))
            : [];
    }
}