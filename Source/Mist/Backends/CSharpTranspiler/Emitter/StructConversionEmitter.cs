using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// Emits the implicit conversions from a flattened struct to each of its ancestors. Flattening
/// leaves the derived struct with no type relationship to what it absorbed, so the assignability
/// Mist's inheritance implies has to be supplied explicitly.
/// <para>
/// A copy, not a reinterpretation. The offsets are not ours to rely on -- see
/// <see cref="FlattenedStruct.Fields"/> -- and a conversion has to produce a value independent of
/// its source in any case.
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
        using var inherited = TypeEmitter.Substituting(ancestor.Path);
        var ancestorFlattened = new StructFlattener(diagnostics).Flatten(ancestor.Definition);
        var members = StructAncestorFactory.Members(ancestorFlattened);
        var factory = StructAncestorFactory.Name(ancestorFlattened);

        // A conversion operator has to name its enclosing type exactly, so both sides carry their
        // type parameters. The ancestor is named as it was inherited rather than by its definition,
        // which is the difference between `Container<T>` and a bare `Container`.
        var derivedName = TypeEmitter.EmitTypeName(derived);
        var ancestorName = TypeNameEmitter.Emit(ancestor.Reference);

        writer.WriteLine($"public static implicit operator {ancestorName}({derivedName} value)");
        using(writer.Block())
        {
            var arguments = DefaultWitnesses.Arguments(DefaultWitnesses.Stored(ancestor.Definition), ancestor.Reference, "value") ?? [];
            arguments.AddRange(ConstructorWitnesses.Arguments(ConstructorWitnesses.Stored(ancestor.Definition), ancestor.Reference, "value"));
            arguments.AddRange(members.Select(member => $"value.{IdentifierSpelling.Of(member.Name)}"));
            arguments.Add("default");

            writer.WriteLine($"return {ancestorName}.{factory}({string.Join(", ", arguments)});");
        }
    }
}