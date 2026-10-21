using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    [ThreadStatic]
    private static LambdaExpression? unitResult;

    [ThreadStatic]
    private static bool returnsUnit;

    private const string UnitReturn = "return Standard.Void.Instance;";

    private static bool YieldsUnit(LambdaExpression lambda)
    {
        return ReferenceEquals(unitResult, lambda);
    }

    private static void EmitReturningUnit(bool unit, Block body, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var enclosing = returnsUnit;
        returnsUnit = unit;

        try
        {
            EmitBlock(body, writer, diagnostics);
        }
        finally
        {
            returnsUnit = enclosing;
        }

        if(unit && body.Statements is not [.., ReturnStatement])
        {
            writer.WriteLine(UnitReturn);
        }
    }

    private static string BareReturn()
    {
        return returnsUnit ? UnitReturn : "return;";
    }

    private static bool EmitUnitResult(
        Expression argument, Parameter? parameter, IMistType? closedTarget, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(parameter is null || !UnitResultSlots.Yields(parameter.Type, closedTarget ?? argument.Type))
        {
            return false;
        }

        switch(argument)
        {
            case LambdaExpression lambda:
                var enclosing = unitResult;
                unitResult = lambda;

                try
                {
                    EmitExpression(lambda, writer, diagnostics);
                }
                finally
                {
                    unitResult = enclosing;
                }

                return true;

            case FunctionReferenceExpression group when group.Type is IFunctionType function:
                writer.Append(RespelledGroups.Lambda(
                    group,
                    group.Receiver is { } receiver && !group.Method.Modifiers.IsStatic ? Render(receiver, diagnostics) : null,
                    UnitResultSlots.Function(function)));
                return true;

            default:
                return false;
        }
    }
}