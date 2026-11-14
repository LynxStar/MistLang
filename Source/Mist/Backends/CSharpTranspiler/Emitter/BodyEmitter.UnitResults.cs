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
    private static string? bareReturn;

    private const string UnitReturn = "return Standard.Void.Instance;";

    private static bool YieldsUnit(LambdaExpression lambda)
    {
        return ReferenceEquals(unitResult, lambda);
    }

    private static void EmitReturningUnit(bool unit, Block body, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        EmitReturning(unit ? UnitReturn : null, body, [], writer, diagnostics);
    }

    internal static void EmitSetterBody(Block body, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        EmitReturning("return value;", body, ["value"], writer, diagnostics);
    }

    private static void EmitReturning(string? returning, Block body, IEnumerable<string> parameterNames, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var enclosing = bareReturn;
        bareReturn = returning;

        try
        {
            EmitBlock(body, parameterNames, writer, diagnostics);
        }
        finally
        {
            bareReturn = enclosing;
        }

        if(returning is not null && body.Statements is not [.., ReturnStatement])
        {
            writer.WriteLine(returning);
        }
    }

    private static string BareReturn()
    {
        return bareReturn ?? "return;";
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