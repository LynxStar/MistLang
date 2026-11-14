using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    private static void EmitReturn(ReturnStatement returnStmt, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(returnStmt.Value is { } value && OptionalUnion.IsVoid(value.Type))
        {
            using var evaluating = AtStatement(value, writer, diagnostics);

            using(var hoisted = EmitHoisted(value, writer, diagnostics, discarded: true))
            {
                if(value is InvocationExpression && !hoisted.Consumes(value))
                {
                    writer.Write("");
                    EmitExpression(value, writer, diagnostics);
                    writer.Append(";");
                    writer.AppendLine();
                }
            }

            writer.WriteLine(BareReturn());
        }
        else if(returnStmt is { IsExitCode: true, Value: { } exitCode })
        {
            EmitSequenced(exitCode, "System.Environment.ExitCode = ", writer, diagnostics, slot: returnStmt.TargetType);
            writer.WriteLine("return;");
        }
        else if(returnStmt.Value is not null)
        {
            EmitSequenced(returnStmt.Value, returnStmt.IsRef ? "return ref " : "return ", writer, diagnostics, slot: returnStmt.TargetType);
        }
        else
        {
            writer.WriteLine(BareReturn());
        }
    }

    internal static void EmitSequenced(
        Expression value,
        string? lead,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        bool branchTaken = false,
        IMistType? slot = null)
    {
        using var evaluating = AtStatement(value, writer, diagnostics);
        using var hoisted = EmitHoisted(value, writer, diagnostics, discarded: lead is null, branchTaken);

        if(hoisted.Consumes(value))
        {
            return;
        }

        writer.Write(lead ?? StatementLead(value));
        EmitEntering(value, slot, writer, diagnostics);
        writer.Append(";");
        writer.AppendLine();
    }

    internal static bool NeedsStatements(Expression value, bool discarded)
    {
        return !UnitHoisting.Plan(value, discarded).IsEmpty || (discarded && EvaluatesCaptures(value));
    }

    internal static UnitHoisting.Hoisting EmitHoisted(
        Expression? root, CSharpWriter writer, DiagnosticBag diagnostics, bool discarded = false, bool branchTaken = false)
    {
        return EmitHoisted(UnitHoisting.Plan(root, discarded, branchTaken), writer, diagnostics);
    }

    internal static UnitHoisting.Hoisting EmitHoisted(
        UnitHoisting.Hoisting hoisting, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        foreach(var unplaced in hoisting.Unplaced)
        {
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.UnhoistedUnitCall, unplaced.Call.Node, null, unplaced.Call.Method is { } called
                    ? DiagnosticSnapshot.Symbol(called, called.Name)
                    : new SymbolValue("<value-call>", "The call"), new EnumValue(unplaced.Reason));
        }

        foreach(var step in hoisting.Steps)
        {
            if(step.Expression is ResultPropagationExpression propagation && step.Kind == UnitHoisting.StepKind.Propagate)
            {
                hoisting.Bind(step, ResultLowering.Emit(propagation, hoisting, writer, diagnostics));
                continue;
            }

            if(step.Kind is UnitHoisting.StepKind.Lower)
            {
                if(ConditionalLowering.Emit(step.Expression, step.Valued, writer, diagnostics) is { } result)
                {
                    hoisting.Bind(step, result);
                }

                continue;
            }

            if(step.Kind is UnitHoisting.StepKind.Hoist)
            {
                if(step.Expression is InvocationExpression { Receiver: MemberAccessExpression { IsConditional: true } } conditional
                    && CompletesPromoted(conditional))
                {
                    ConditionalAsyncCalls.Emit(conditional, blockingAwaits, statement: true, writer, diagnostics, EmitDirect);
                }
                else
                {
                    using var evaluating = AtStatement(step.Expression, writer, diagnostics);
                    writer.WriteLine($"{Render(step.Expression, diagnostics)};");
                }

                hoisting.Bind(step, "Standard.Void.Instance");
                continue;
            }

            var temporary = TemporaryName();
            var text = Render(step.Expression, diagnostics);
            writer.WriteLine($"{TypeNameEmitter.EmitInferable(step.Expression.Type)} {temporary} = {text};");
            hoisting.Bind(step, temporary);
        }

        return hoisting;
    }
}