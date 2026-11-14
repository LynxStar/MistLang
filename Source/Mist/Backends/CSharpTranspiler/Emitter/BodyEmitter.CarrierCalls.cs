using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.Backends.CSharpTranspiler.Emitter;

internal static partial class BodyEmitter
{
    private static void EmitCarrierCall(
        InvocationExpression call, MemberAccessExpression callee, string carrier, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var method = call.Method!;
        var (arguments, parameters) = ExtensionBlockEmitter.CallTypeArguments(method, callee.TypeArguments);
        writer.Append($"{carrier}.{CalleeName(OverloadNames.Of(method), arguments, parameters)}{EmitTypeArguments(arguments, parameters)}(");
        if(!method.Modifiers.IsStatic)
        {
            EmitExpression(callee.Receiver, writer, diagnostics);

            if(ExtensionCalls.NeedsNonNullReceiver(callee.Receiver.Type, method))
            {
                writer.Append("!");
            }
        }

        EmitWitnesses(call, writer, diagnostics, afterReceiver: !method.Modifiers.IsStatic);
        EmitArguments(call.Arguments, method.ParametersList, call.ParameterTypes, writer, diagnostics, method);
        writer.Append(")");
    }
}