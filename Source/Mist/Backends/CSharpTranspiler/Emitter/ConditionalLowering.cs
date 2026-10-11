using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// A conditional whose operand that may not run takes the value of a call yielding nothing, run as statements
/// in place of the expression C# would have evaluated: the operand sits inside the branch that runs it, where
/// <see cref="UnitHoisting"/> can put the call ahead of it. Answers the temporary holding the conditional's
/// value, or null where the value is not taken.
/// <para>
/// What runs before the branch is written where C# would have evaluated it, once. A logical chain nests its
/// links as <see cref="GuardChain"/> does. A conditional access reads its receiver twice and a <c>??=</c> its
/// target, so the planner stores what they are made of ahead of the lowering.
/// </para>
/// </summary>
internal static class ConditionalLowering
{
    public static string? Emit(Expression expression, bool valued, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        return expression switch
        {
            BinaryExpression chain => EmitLogical(chain, valued, writer, diagnostics),
            CoalesceExpression coalesce => EmitCoalesce(coalesce, writer, diagnostics),
            AssignmentExpression assignment => EmitCoalescingAssignment(assignment, valued, writer, diagnostics),
            InvocationExpression call => EmitConditionalCall(call, valued, writer, diagnostics),
            SwitchExpression switchExpression => EmitSwitch(switchExpression, valued, writer, diagnostics),
            _ => throw new InvalidOperationException($"ICE: no lowering for a conditional {expression.GetType().Name}."),
        };
    }

    private static string? EmitLogical(BinaryExpression chain, bool valued, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var result = valued ? BodyEmitter.TemporaryName() : null;
        var conjunction = chain.OperatorKind is BinaryOperatorKind.LogicalAnd;

        if(result is not null)
        {
            writer.WriteLine($"bool {result} = {(conjunction ? "false" : "true")};");
        }

        GuardChain.EmitNested(GuardChain.Split(chain, chain.OperatorKind), chain.OperatorKind, writer, diagnostics, () =>
        {
            if(result is not null)
            {
                writer.WriteLine($"{result} = {(conjunction ? "true" : "false")};");
            }
        });

        return result;
    }

    private static string EmitCoalesce(CoalesceExpression coalesce, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var result = BodyEmitter.TemporaryName();
        var present = BodyEmitter.TemporaryName();

        writer.WriteLine($"{TypeNameEmitter.EmitInferable(coalesce.Type)} {result};");
        writer.WriteLine($"if ({BodyEmitter.RenderAt(coalesce.Left, BodyEmitter.RelationalPrecedence, diagnostics)} is {{ }} {present})");

        using(writer.Block())
        {
            writer.WriteLine($"{result} = {present};");
        }

        writer.WriteLine("else");

        using(writer.Block())
        {
            BodyEmitter.EmitSequenced(coalesce.Right, Assigning(result), writer, diagnostics, slot: coalesce.Type);
        }

        return result;
    }

    private static string? EmitCoalescingAssignment(
        AssignmentExpression assignment, bool valued, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var target = assignment.Target is LocalReferenceExpression local
            ? LocalNames.Of(local.Local)
            : BodyEmitter.Render(assignment.Target, diagnostics)
            ;

        writer.WriteLine($"if ({target} is null)");

        using(writer.Block())
        {
            BodyEmitter.EmitSequenced(assignment.Value, Assigning(target), writer, diagnostics, slot: assignment.Target.Type);
        }

        return valued ? target : null;
    }

    private static string? EmitConditionalCall(
        InvocationExpression call, bool valued, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var receiver = ((MemberAccessExpression)call.Receiver).Receiver;
        var result = valued ? BodyEmitter.TemporaryName() : null;

        if(result is not null)
        {
            writer.WriteLine($"{TypeNameEmitter.EmitInferable(call.Type)} {result} = default;");
        }

        writer.WriteLine($"if ({BodyEmitter.RenderAt(receiver, BodyEmitter.RelationalPrecedence, diagnostics)} is not null)");

        using(writer.Block())
        {
            BodyEmitter.EmitSequenced(call, Assigning(result), writer, diagnostics, branchTaken: true);
        }

        return result;
    }

    private static string? EmitSwitch(
        SwitchExpression switchExpression, bool valued, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var result = valued ? BodyEmitter.TemporaryName() : null;
        var narrowed = BodyEmitter.NarrowedUnion(switchExpression);

        if(result is not null)
        {
            writer.WriteLine($"{TypeNameEmitter.EmitInferable(switchExpression.Type)} {result};");
        }

        writer.Write("switch (");
        BodyEmitter.EmitScrutinee(switchExpression.Scrutinee, 0, writer, diagnostics);
        writer.Append(")");
        writer.AppendLine();

        using(writer.Block())
        {
            foreach(var arm in switchExpression.Arms)
            {
                writer.Write("case ");
                BodyEmitter.EmitArmHead(arm, narrowed, writer, diagnostics);
                writer.Append(":");
                writer.AppendLine();

                using(writer.Block())
                {
                    BodyEmitter.EmitSequenced(arm.Result, Assigning(result) ?? Discarding(arm.Result), writer, diagnostics, slot: switchExpression.Type);
                    writer.WriteLine("break;");
                }
            }

            writer.WriteLine("default:");
            writer.Indent();
            writer.WriteLine("throw new System.Runtime.CompilerServices.SwitchExpressionException();");
            writer.Dedent();
        }

        return result;
    }

    private static string? Assigning(string? target)
    {
        return target is null ? null : $"{target} = ";
    }

    private static string? Discarding(Expression result)
    {
        return result is InvocationExpression or AssignmentExpression ? null : "_ = ";
    }
}