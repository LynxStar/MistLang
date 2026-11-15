using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The signature is built from <see cref="CastOperator.IsImplicit"/> and
/// <see cref="CastOperator.TargetType"/> rather than from the member's name, which encodes both
/// (<c>op_Implicit_{Target}</c>) so that the kinds and targets stay distinct in a list keyed by name,
/// but is not a C# identifier.
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

        var method = OperatorMethods.Applies(castOperator);
        using var reach = method ? null : SlotReach.OfOperator(castOperator);
        var modifiers = MemberModifierEmitter.Emit(castOperator.Modifiers, emitReadonly: false);
        var kind = castOperator.IsImplicit ? "implicit" : "explicit";
        var nullableCopy = castOperator.CopiesClone && TypeNameEmitter.IsReferenceKind(castOperator.TargetType);
        var targetType = nullableCopy ? TypeNameEmitter.EmitOptional(castOperator.TargetType) : TypeNameEmitter.Emit(castOperator.TargetType);
        var source = castOperator.SourceParameter;
        var parameters = nullableCopy
            ? AttributeEmitter.Inline(source.DeclaredAttributesList, diagnostics)
                + $"{TypeNameEmitter.EmitOptional(source.Type)} {IdentifierSpelling.Of(source.Name)}"
            : ParameterEmitter.Emit(castOperator.ParametersList, diagnostics);
        var hidden = method ? SlotRoutes.HiddenParameters(SlotRoutes.ValueRouted(castOperator.DeclaringType)).ToList() : [];

        if(nullableCopy)
        {
            writer.WriteLine($"[return: System.Diagnostics.CodeAnalysis.NotNullIfNotNull(\"{source.Name}\")]");
        }

        writer.WriteLine(method
            ? $"{modifiers}{targetType} {OperatorMethods.Name(castOperator)}({string.Join(", ", hidden.Append(parameters))})"
            : $"{modifiers}{kind} operator {targetType}({parameters})");
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