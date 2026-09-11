using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a user-declared operator. The signature is built from <see cref="Operator.Symbol"/> rather than
/// from the member's name, which is the CLR spelling (<c>op_Addition</c>) and not a C# identifier -- the
/// same reason <see cref="CastOperatorEmitter"/> builds its own from the target type.
/// </summary>
internal static class OperatorEmitter
{
    public static void Emit(Operator op, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = MemberModifierEmitter.Emit(op.Modifiers, emitReadonly: false);
        var returnType = MethodEmitter.EmitReturnType(op);
        var parameters = ParameterEmitter.Emit(op.ParametersList, diagnostics);

        AttributeEmitter.EmitLines(op.DeclaredAttributesList, writer, diagnostics);
        writer.WriteLine($"{modifiers}{returnType} operator {op.Symbol}({parameters})");
        using(writer.Block())
        {
            if(op.Body is not null)
            {
                BodyEmitter.EmitMethodBody(op, op.Body, writer, diagnostics);
            }
        }
    }
}