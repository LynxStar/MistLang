using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The body of a conversion between a newtype's clone and its copyable source: a new value of the target
/// holding every field of the operand. The two are unrelated C# types that each keep their state private,
/// so each field is reached through an UnsafeAccessor rather than by name -- no reflection, and nothing the
/// program's own code may touch is widened. A class is allocated uninitialized, every field being written
/// afterwards; a constructor would run the source's code a second time.
/// </summary>
internal static class CloneCopyEmitter
{
    public static void EmitBody(CastOperator conversion, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var from = (ConcreteType)conversion.SourceParameter.Type;
        var to = (ConcreteType)conversion.TargetType;
        var target = TypeNameEmitter.Emit(to);

        writer.WriteLine(to is StructType
            ? $"var copy = default({target});"
            : $"var copy = ({target})System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof({target}));");

        foreach(var (write, read) in Slots(to, diagnostics).Zip(Slots(from, diagnostics)))
        {
            writer.WriteLine($"{Call(write, "copy")} = {Call(read, conversion.SourceParameter.Name)};");
        }

        writer.WriteLine("return copy;");
    }

    public static void EmitAccessors(ConcreteType clone, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var slots = Slots(clone, diagnostics).Concat(Slots(clone.CloneSource!, diagnostics)).DistinctBy(AccessorName);

        foreach(var slot in slots)
        {
            var owner = TypeNameEmitter.Emit(slot.Owner);
            var receiver = slot.Owner is StructType ? $"ref {owner}" : owner;

            writer.WriteLine(
                $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"{slot.Storage}\")]");
            writer.WriteLine($"private static extern ref {TypeNameEmitter.Emit(slot.Type)} {AccessorName(slot)}({receiver} target);");
        }
    }

    private static string Call(Slot slot, string variable)
    {
        return slot.Owner is StructType
            ? $"{AccessorName(slot)}(ref {variable})"
            : $"{AccessorName(slot)}({variable})";
    }

    private static string AccessorName(Slot slot)
    {
        var owner = slot.Owner.FullyQualifiedName.Replace('.', '_');
        var storage = slot.Storage.Replace("<", "", StringComparison.Ordinal).Replace(">k__", "_", StringComparison.Ordinal);

        return $"__{owner}_{storage}";
    }

    private static IEnumerable<Slot> Slots(ConcreteType type, DiagnosticBag diagnostics)
    {
        if(type is StructType structType)
        {
            var flattened = new StructFlattener(diagnostics).Flatten(structType);

            return flattened.Fields.Where(field => !field.Field.Modifiers.IsStatic)
                .Select(field => new Slot(type, field.Name, field.Type))
                .Concat(AutoProperties(type, flattened.Properties));
        }

        var chain = new List<ConcreteType>();

        for(var current = type; current is { DeclarationNode: not null }; current = current.BaseType as ConcreteType)
        {
            chain.Insert(0, current);
        }

        return chain.SelectMany(owner => owner.FieldsList.Where(field => !field.Modifiers.IsStatic)
            .Select(field => new Slot(owner, field.Name, field.Type))
            .Concat(AutoProperties(owner, owner.PropertiesList)));
    }

    private static IEnumerable<Slot> AutoProperties(ConcreteType owner, IEnumerable<Property> properties)
    {
        return properties
            .Where(property => !property.Modifiers.IsStatic
                && AccessorEmitter.IsBodiless(property.Getter)
                && AccessorEmitter.IsBodiless(property.Setter))
            .Select(property => new Slot(owner, $"<{property.Name}>k__BackingField", property.Type));
    }

    private sealed record Slot(ConcreteType Owner, string Storage, IMistType Type);
}