using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
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
        var from = conversion.SourceParameter.Type;
        var to = conversion.TargetType;
        var target = TypeNameEmitter.Emit(to);

        writer.WriteLine(Definition(to) is StructType
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
        var slots = Slots(clone, diagnostics)
            .Concat(Slots(clone.CloneSourceType!, diagnostics))
            .DistinctBy(slot => (slot.Definition, slot.Storage));

        var owners = slots.GroupBy(slot => slot.Definition).ToList();

        EmitDeclarations(owners.Where(owner => owner.Key.TypeParametersList.Count == 0).SelectMany(owner => owner), "private", AccessorName, writer);

        foreach(var owner in owners.Where(owner => owner.Key.TypeParametersList.Count > 0))
        {
            writer.WriteBlankLine();
            writer.WriteLine($"private static class {Holder(owner.Key)}<{string.Join(", ", owner.Key.TypeParametersList.Select(TypeNameEmitter.Emit))}>");

            using(writer.Block())
            {
                EmitDeclarations(owner, "public", slot => $"__{Sanitized(slot.Storage)}", writer);
            }
        }
    }

    private static void EmitDeclarations(IEnumerable<Slot> slots, string accessibility, Func<Slot, string> name, CSharpWriter writer)
    {
        foreach(var slot in slots)
        {
            var owner = TypeNameEmitter.Emit(Itself(slot.Definition));
            var receiver = slot.Definition is StructType ? $"ref {owner}" : owner;

            writer.WriteLine(
                $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"{slot.Storage}\")]");
            writer.WriteLine($"{accessibility} static extern ref {TypeNameEmitter.Emit(slot.Type)} {name(slot)}({receiver} target);");
        }
    }

    private static string Call(Slot slot, string variable)
    {
        var accessor = slot.Owner is ConstructedType { TypeArguments: var arguments }
            ? $"{Holder(slot.Definition)}<{string.Join(", ", arguments.Select(TypeNameEmitter.Emit))}>.__{Sanitized(slot.Storage)}"
            : AccessorName(slot);

        return slot.Definition is StructType
            ? $"{accessor}(ref {variable})"
            : $"{accessor}({variable})";
    }

    private static string AccessorName(Slot slot)
    {
        return $"__{slot.Definition.FullyQualifiedName.Replace('.', '_')}_{Sanitized(slot.Storage)}";
    }

    private static string Holder(ConcreteType definition)
    {
        var stem = definition.FullyQualifiedName.Where(character => character is not ('<' or '>' or ',' or ' '));

        return $"__{new string([.. stem.Select(character => char.IsLetterOrDigit(character) ? character : '_')])}_Access";
    }

    private static string Sanitized(string storage)
    {
        return storage.Replace("<", "", StringComparison.Ordinal).Replace(">k__", "_", StringComparison.Ordinal);
    }

    private static IEnumerable<Slot> Slots(IMistType type, DiagnosticBag diagnostics)
    {
        if(Definition(type) is StructType structType)
        {
            var flattened = new StructFlattener(diagnostics).Flatten(structType);

            return flattened.Fields.Where(field => !field.Field.Modifiers.IsStatic)
                .Select(field => new Slot(structType, type, field.Name, field.Type))
                .Concat(AutoProperties(structType, type, flattened.Properties));
        }

        var chain = new List<(ConcreteType Definition, IMistType Owner)>();

        for(var current = (IMistType?)type; current is not null && Definition(current) is ConcreteType { DeclarationNode: not null } owner; current = BaseOf(current, owner))
        {
            chain.Insert(0, (owner, current));
        }

        return chain.SelectMany(link => link.Definition.FieldsList.Where(field => !field.Modifiers.IsStatic)
            .Select(field => new Slot(link.Definition, link.Owner, field.Name, field.Type))
            .Concat(AutoProperties(link.Definition, link.Owner, link.Definition.PropertiesList)));
    }

    private static IMistType? BaseOf(IMistType type, ConcreteType definition)
    {
        return type is ConstructedType constructed && definition.BaseType is { } baseType
            ? TypeSubstitution.ForReceiver(constructed).Apply(baseType, interner: null)
            : definition.BaseType;
    }

    private static IMistType Itself(ConcreteType definition)
    {
        return definition.TypeParametersList.Count == 0 ? definition : new ConstructedType(definition, definition.TypeParametersList);
    }

    private static IMistType Definition(IMistType type)
    {
        return type is ConstructedType constructed ? constructed.Definition : type;
    }

    private static IEnumerable<Slot> AutoProperties(ConcreteType definition, IMistType owner, IEnumerable<Property> properties)
    {
        return properties
            .Where(property => !property.Modifiers.IsStatic
                && AccessorEmitter.IsBodiless(property.Getter)
                && AccessorEmitter.IsBodiless(property.Setter))
            .Select(property => new Slot(definition, owner, $"<{property.Name}>k__BackingField", property.Type));
    }

    private sealed record Slot(ConcreteType Definition, IMistType Owner, string Storage, IMistType Type);
}