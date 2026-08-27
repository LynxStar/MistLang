using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a cast operator declaration. The signature is built from
/// <see cref="CastOperator.IsImplicit"/> and <see cref="CastOperator.TargetType"/> rather than from
/// the member's name, which encodes both but is not a C# identifier: a cast operator names itself
/// <c>op_Implicit_{Target}</c> so that the two kinds and the several targets stay distinct in a list
/// keyed by name.
/// </summary>
internal static class CastOperatorEmitter
{
    public static void Emit(CastOperator castOperator, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var modifiers = MemberModifierEmitter.Emit(castOperator.Modifiers, emitReadonly: false);
        var kind = castOperator.IsImplicit ? "implicit" : "explicit";
        var targetType = TypeNameEmitter.Emit(castOperator.TargetType);
        var parameters = ParameterEmitter.Emit(castOperator.ParametersList);

        writer.WriteLine($"{modifiers}{kind} operator {targetType}({parameters})");
        using(writer.Block())
        {
            if(castOperator.Body is not null)
            {
                BodyEmitter.EmitBlock(castOperator.Body, writer, diagnostics);
            }
        }
    }
}