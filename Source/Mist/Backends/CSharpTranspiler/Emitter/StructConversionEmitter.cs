using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits the implicit conversions from a flattened struct to each of its ancestors. Flattening
/// leaves the derived struct with no type relationship to what it absorbed, so the assignability
/// Mist's inheritance implies has to be supplied explicitly.
/// <para>
/// A copy, not a reinterpretation. The offsets <see cref="FlattenedStruct.Fields"/> guarantees make
/// <c>Unsafe.As</c> valid here and cheaper, but that aliases the original, and a conversion has to
/// produce an independent value -- so reinterpreting stays the caller's choice to make.
/// </para>
/// </summary>
internal static class StructConversionEmitter
{
    public static void Emit(
        StructType derived,
        AbsorbedAncestor ancestor,
        CSharpWriter writer,
        DiagnosticBag diagnostics)
    {
        var ancestorFields = new StructFlattener(diagnostics).Flatten(ancestor.Definition).Fields;

        // A conversion operator has to name its enclosing type exactly, so both sides carry their
        // type parameters. The ancestor is named as it was inherited rather than by its definition,
        // which is the difference between `Container<T>` and a bare `Container`.
        var derivedName = TypeEmitter.EmitTypeName(derived);
        var ancestorName = TypeNameEmitter.Emit(ancestor.Reference);

        foreach(var field in ancestorFields)
        {
            // The operator is written on the DERIVED struct, so a field the ancestor keeps to
            // itself is out of reach, and a readonly one cannot be assigned after `default`.
            if(field.Accessibility == Accessibility.Private || field.Field.Modifiers.IsReadonly)
            {
                Report(diagnostics, derived, field.Name, ancestorName, derivedName);
                return;
            }
        }

        writer.WriteLine($"public static implicit operator {ancestorName}({derivedName} value)");
        using(writer.Block())
        {
            // `default` then assign, rather than calling a constructor: no constructor is
            // guaranteed to take exactly the ancestor's fields, and one that does other work would
            // do it twice.
            writer.WriteLine($"{ancestorName} result = default;");

            foreach(var field in ancestorFields)
            {
                writer.WriteLine($"result.{field.Name} = value.{field.Name};");
            }

            writer.WriteLine("return result;");
        }
    }

    private static void Report(
        DiagnosticBag diagnostics,
        StructType derived,
        string fieldName,
        string ancestorName,
        string derivedName)
    {
        if(derived.DeclarationNode is { } node)
        {
            diagnostics.ReportAtNode(
                BackendDiagnostics.StructConversionFieldInaccessible, node, null,
                ancestorName, derivedName, fieldName);
            return;
        }

        diagnostics.Report(
            BackendDiagnostics.StructConversionFieldInaccessible, default, "<unknown>",
            ancestorName, derivedName, fieldName);
    }
}