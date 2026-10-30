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
/// afterwards; a constructor would run the source's code a second time. A source emitted per instantiation
/// (<see cref="UnionSpecialization"/>) is reached as the closed type it is in C#, not through a holder over
/// parameters it no longer declares.
/// </summary>
internal static class CloneCopyEmitter
{
    public static void EmitBody(CastOperator conversion, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var from = conversion.SourceParameter.Type;
        var to = conversion.TargetType;
        var target = TypeNameEmitter.Emit(to);

        if(Definition(to) is not StructType)
        {
            writer.WriteLine($"if({IdentifierSpelling.Of(conversion.SourceParameter.Name)} is null)");

            using(writer.Block())
            {
                writer.WriteLine("return null;");
            }

            writer.WriteBlankLine();
        }

        writer.WriteLine(Definition(to) is StructType
            ? $"var copy = default({target});"
            : $"var copy = ({target})System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof({target}));");

        var clone = (ConcreteType)conversion.DeclaringType!;

        foreach(var (write, read) in Slots(to, diagnostics).Zip(Slots(from, diagnostics)))
        {
            writer.WriteLine($"{Call(clone, write, "copy")} = {Call(clone, read, IdentifierSpelling.Of(conversion.SourceParameter.Name))};");
        }

        writer.WriteLine("return copy;");
    }

    public static void EmitAccessors(ConcreteType clone, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var slots = Slots(Itself(clone), diagnostics)
            .Concat(Slots(clone.CloneSourceType!, diagnostics))
            .DistinctBy(slot => (slot.Definition, Stem(slot), slot.Storage));

        var owners = slots.GroupBy(slot => slot.Definition).ToList();

        foreach(var line in owners.Where(owner => IsFlat(clone, owner.Key)).SelectMany(owner => Declarations(owner, "private", AccessorName)))
        {
            writer.WriteLine(line);
        }

        foreach(var owner in owners.Where(owner => !IsFlat(clone, owner.Key)))
        {
            var members = Declarations(owner, "public", slot => $"__{Sanitized(slot.Storage)}");

            new AccessorHolder(Holder(clone, owner.Key), owner.Key, [.. members]).Place(clone, writer);
        }
    }

    private static List<string> Declarations(IEnumerable<Slot> slots, string accessibility, Func<Slot, string> name)
    {
        var lines = new List<string>();

        foreach(var slot in slots)
        {
            using var closing = Instance(slot) is { } instance
                ? TypeNameEmitter.Substituting(TypeSubstitution.ForReceiver(instance))
                : null;
            var owner = TypeNameEmitter.Emit(Itself(slot.Definition));
            var receiver = slot.Definition is StructType ? $"ref {owner}" : owner;

            lines.Add(
                $"[System.Runtime.CompilerServices.UnsafeAccessor(System.Runtime.CompilerServices.UnsafeAccessorKind.Field, Name = \"{slot.Storage}\")]");
            lines.Add($"{accessibility} static extern ref {TypeNameEmitter.Emit(slot.Type)} {name(slot)}({receiver} target);");
        }

        return lines;
    }

    private static string Call(ConcreteType clone, Slot slot, string variable)
    {
        var accessor = slot.Owner is ConstructedType { TypeArguments: var arguments } && !IsFlat(clone, slot.Definition)
            ? $"{Holder(clone, slot.Definition)}<{string.Join(", ", arguments.Select(TypeNameEmitter.Emit))}>.__{Sanitized(slot.Storage)}"
            : AccessorName(slot);

        return slot.Definition is StructType
            ? $"{accessor}(ref {variable})"
            : $"{accessor}({variable})";
    }

    private static string AccessorName(Slot slot)
    {
        return $"__{Stem(slot)}_{Sanitized(slot.Storage)}";
    }

    private static string Stem(Slot slot)
    {
        return AccessorHolder.Flat(Instance(slot) ?? (IMistType)slot.Definition);
    }

    private static bool IsFlat(ConcreteType clone, ConcreteType definition)
    {
        return definition.TypeParametersList.Count == 0 || UnionSpecialization.Specializes(definition) || ReferenceEquals(definition, clone);
    }

    private static ConstructedType? Instance(Slot slot)
    {
        return slot.Owner is ConstructedType construction && UnionSpecialization.NameOf(construction) is not null
            ? construction
            : null
            ;
    }

    private static string Holder(ConcreteType clone, ConcreteType definition)
    {
        return AccessorHolder.NameFor(clone, $"__{AccessorHolder.Flat(definition)}_Access");
    }

    private static string Sanitized(string storage)
    {
        return storage.Replace("<", "", StringComparison.Ordinal).Replace(">k__", "_", StringComparison.Ordinal);
    }

    private static IEnumerable<Slot> Slots(IMistType type, DiagnosticBag diagnostics)
    {
        type = TypeNameEmitter.Closed(type);

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