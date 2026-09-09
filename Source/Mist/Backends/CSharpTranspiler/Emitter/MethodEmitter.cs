using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits a method declaration: modifiers, return type, name, parameters, and body.
/// </summary>
internal static class MethodEmitter
{
    public static void Emit(
        Method method,
        bool inStruct,
        CSharpWriter writer,
        DiagnosticBag diagnostics,
        MemberType? owner = null)
    {
        // A readonly method is a struct-only concept in C#, so on a class the modifier is dropped
        // rather than passed through -- Mist admits it there and CS0106 refuses it.
        var modifiers = MemberModifierEmitter.Emit(
            method.Modifiers, emitReadonly: inStruct && method.Modifiers.IsReadonly)
            + (AsyncPromotion.EmitsAsync(method) ? "async " : "");

        AttributeEmitter.EmitLines(method.DeclaredAttributesList, writer, diagnostics);
        writer.WriteLine($"{modifiers}{EmitSignature(method, diagnostics)}");
        ConstraintEmitter.Emit(method.TypeParametersList, writer);
        using(writer.Block())
        {
            if(method.Body is not null)
            {
                BodyEmitter.EmitMethodBody(method, method.Body, writer, diagnostics);
                EmitOwedFieldDrops(method, owner, writer);
            }
        }
    }

    /// <summary>
    /// What a declared <c>Drop</c> still owes its fields, appended after whatever the author wrote.
    /// See <see cref="DropInsertion.FieldsOwedBy"/> for why writing a Drop does not discharge it.
    /// </summary>
    private static void EmitOwedFieldDrops(Method method, MemberType? owner, CSharpWriter writer)
    {
        if(owner is not ConcreteType concrete)
        {
            return;
        }

        foreach(var field in DropInsertion.FieldsOwedBy(method, concrete))
        {
            writer.WriteLine(DropInsertion.FieldDrop(field));
        }
    }

    /// <summary>
    /// Refuses the one <c>ref</c> spelling <see cref="EmitSignature"/> cannot resolve. Kept apart from
    /// the signature because that is also spelled for an interface member and for a restated default,
    /// so the report belongs at the sites that OWN a declaration -- restating someone else's must not
    /// report it twice.
    /// </summary>
    internal static void ReportReadonlyRefReturn(
        Method method, Storm.Runtime.SyntaxNode? locationNode, DiagnosticBag diagnostics)
    {
        if(method.Modifiers.IsRef && method.Modifiers.IsReadonly && locationNode is not null)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.ReadonlyRefReturn, locationNode, null, method.Name);
        }
    }

    /// <summary>
    /// Everything of a method declaration but its modifiers and its body, shared with the
    /// interface member path in <see cref="TypeEmitter"/> so the two cannot disagree about
    /// what a method's signature contains.
    /// </summary>
    internal static string EmitSignature(Method method, DiagnosticBag diagnostics)
    {
        // The one deliberate exception to Void emitting as Standard.Void: a return type takes
        // the native C# form, and the call-site lowering in BodyEmitter reconstructs the unit
        // value wherever a caller consumes it -- exact, because the type has one inhabitant.
        // Only the bare ref, never `ref readonly`: on a method `readonly` is ALREADY the struct
        // modifier that MemberModifierEmitter writes, so the pair has two readings and
        // ReadonlyRefReturn refuses it rather than either being picked here.
        // A promoted method's emitted return type is a task the MODEL never mentions: Mist declared it
        // sync, and this is only how C# is made to wait. See AsyncPromotion.
        var emitted = AsyncPromotion.IsPromoted(method)
            ? AsyncPromotion.WrapReturn(method.ReturnType)
            : method.ReturnType
            ;

        var returnType = OptionalUnion.IsVoid(emitted)
            ? "void"
            : $"{RefKeywords.For(method.Modifiers.IsRef, isReadonly: false)}{TypeNameEmitter.Emit(emitted)}";
        // A specialization declares the pack's expansion in the pack's place and carries the width
        // in its name, exactly as a specialized TYPE does -- and for the same reason: the widths
        // differ in what they declare, so they cannot be one C# method.
        var declared = method.TypeParametersList;
        var name = method.Name;

        if(Monomorph.InScope() is { } monomorph && monomorph.Owns(method.TypeParametersList))
        {
            declared = [.. monomorph.Parameters(method.TypeParametersList)];
            name = MonomorphSet.SpecializedName(method.Name, monomorph.Widths);
        }

        var typeParams = declared.Count > 0
            ? $"<{string.Join(", ", declared.Select(DependentCarrierNames.Parameter))}>"
            : "";
        var parts = DefaultConstraintWitness.Parameters(declared);
        var parameters = ParameterEmitter.Emit(method.ParametersList, diagnostics);

        if(parameters.Length > 0)
        {
            parts.Add(parameters);
        }

        return $"{returnType} {name}{typeParams}({string.Join(", ", parts)})";
    }
}