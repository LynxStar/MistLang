using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static class ConditionalAsyncCalls
{
    public static void Emit(
        InvocationExpression call, bool blocking, bool statement, CSharpWriter writer, DiagnosticBag diagnostics,
        Action<Expression, CSharpWriter, DiagnosticBag> emitDirect)
    {
        var access = (MemberAccessExpression)call.Receiver;
        var present = BodyEmitter.TemporaryName();
        var local = new LocalVariable(present, OptionalUnion.InnerType(access.Receiver.Type) ?? access.Receiver.Type);
        var target = new MemberAccessExpression(access.Node,
            new LocalReferenceExpression(access.Receiver.Node, local), access.MemberName, access.Type,
            access.TypeArguments, member: access.Member);
        var invocation = new InvocationExpression(call.Node, target, call.Arguments,
            OptionalUnion.InnerType(call.Type) ?? call.Type, call.Method, call.ParameterTypes)
        {
            ContextTypes = call.ContextTypes,
            InvokeMethod = call.InvokeMethod,
            IsImplicit = call.IsImplicit,
        };
        var receiver = BodyEmitter.RenderAt(access.Receiver, BodyEmitter.RelationalPrecedence, diagnostics);

        if(statement)
        {
            writer.WriteLine($"if ({receiver} is {{ }} {present})");
            using(writer.Block())
            {
                using var hoisted = BodyEmitter.EmitHoisted(invocation, writer, diagnostics, discarded: true);
                writer.Write("");
                EmitCompleted(invocation, blocking, writer, diagnostics, emitDirect);
                writer.Append(";");
                writer.AppendLine();
            }

            return;
        }

        writer.Append($"({receiver} is {{ }} {present} ? ");
        EmitCompleted(invocation, blocking, writer, diagnostics, emitDirect);
        writer.Append($" : default({TypeNameEmitter.Emit(call.Type)}))");
    }

    private static void EmitCompleted(
        InvocationExpression call, bool blocking, CSharpWriter writer, DiagnosticBag diagnostics,
        Action<Expression, CSharpWriter, DiagnosticBag> emitDirect)
    {
        if(!blocking)
        {
            writer.Append("await ");
        }

        emitDirect(call, writer, diagnostics);

        if(blocking)
        {
            writer.Append(".GetAwaiter().GetResult()");
        }
    }
}