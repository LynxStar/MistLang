using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    private static void EmitCarrierCall(
        InvocationExpression call, MemberAccessExpression callee, string carrier, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var (arguments, parameters) = ExtensionBlockEmitter.CallTypeArguments(call.Method, callee.TypeArguments);
        writer.Append($"{carrier}.{CalleeName(call.Method!.Name, arguments, parameters)}{EmitTypeArguments(arguments, parameters)}(");
        if(!call.Method.Modifiers.IsStatic)
        {
            EmitExpression(callee.Receiver, writer, diagnostics);

            if(ExtensionCalls.NeedsNonNullReceiver(callee.Receiver.Type, call.Method))
            {
                writer.Append("!");
            }

            if(HasArguments(call.Arguments, call.Method.ParametersList)
                || DefaultWitnesses.MethodArguments(call.Method, callee.TypeArguments, callee.Receiver.Type, call.ContextTypes).Count > 0)
            {
                writer.Append(", ");
            }
        }

        EmitWitnesses(call, writer, diagnostics);
        EmitArguments(call.Arguments, call.Method.ParametersList, call.ParameterTypes, writer, diagnostics, call.Method);
        writer.Append(")");
    }
}