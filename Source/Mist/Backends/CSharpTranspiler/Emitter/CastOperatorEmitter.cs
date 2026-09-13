using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

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
        if(castOperator.ClonesThrough is not null)
        {
            CloneConversionEmitter.Emit(castOperator, writer, diagnostics);
            return;
        }

        if(IntrinsicCloneEmitter.Wraps(castOperator.DeclaringType)
            && (castOperator.CopiesClone || castOperator.Modifiers.IsIntrinsic))
        {
            IntrinsicCloneEmitter.EmitCastOperator(castOperator, writer, diagnostics);
            return;
        }

        var modifiers = MemberModifierEmitter.Emit(castOperator.Modifiers, emitReadonly: false);
        var kind = castOperator.IsImplicit ? "implicit" : "explicit";
        var targetType = TypeNameEmitter.Emit(castOperator.TargetType);
        var parameters = ParameterEmitter.Emit(castOperator.ParametersList, diagnostics);

        writer.WriteLine($"{modifiers}{kind} operator {targetType}({parameters})");
        using(writer.Block())
        {
            if(castOperator.CopiesClone)
            {
                CloneCopyEmitter.EmitBody(castOperator, writer, diagnostics);
            }
            else if(castOperator.Body is not null)
            {
                BodyEmitter.EmitBlock(castOperator.Body, writer, diagnostics);
            }
        }

        if(castOperator is { CopiesClone: true, IsImplicit: true, DeclaringType: ConcreteType clone })
        {
            writer.WriteBlankLine();
            CloneCopyEmitter.EmitAccessors(clone, writer, diagnostics);
        }
    }
}