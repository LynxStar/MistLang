using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static bool withinUnchecked;

    private static void EmitUnchecked(Expression expression, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(withinUnchecked || !ConstantArithmetic.Overflows(expression))
        {
            EmitDirect(expression, writer, diagnostics);
            return;
        }

        withinUnchecked = true;

        try
        {
            writer.Append("unchecked(");
            EmitDirect(expression, writer, diagnostics);
            writer.Append(")");
        }
        finally
        {
            withinUnchecked = false;
        }
    }

    private static string DividedByZero(BinaryExpression division, DiagnosticBag diagnostics)
    {
        var divisor = $"(({TypeNameEmitter.Emit(division.Right.Type)})(object)({Render(division.Right, diagnostics)}))";

        return NarrowArithmetic.Spell(
            division.Type,
            $"{RenderAt(division.Left, GetPrecedence(division.OperatorKind), diagnostics)} {MapBinaryOperator(division.OperatorKind)} {divisor}");
    }
}