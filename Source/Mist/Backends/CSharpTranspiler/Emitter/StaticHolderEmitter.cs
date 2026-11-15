using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The holder <see cref="StaticHolders"/> describes, written inside the value-routed type it belongs to, with the
/// table and the lookup beside it. A member of the holder is reached from the type's own members and from every
/// other type, so it is public where the static was public or protected and internal otherwise, and an accessor the
/// static narrowed to private is narrowed only as far as the holder's own reach allows.
/// </summary>
internal static class StaticHolderEmitter
{
    public static void Emit(ConcreteType type, FlattenedStruct? flattened, bool inReadonlyType, CSharpWriter writer, DiagnosticBag diagnostics, ref bool anyEmitted)
    {
        var held = (flattened?.Fields.Select(field => field.Field) ?? type.FieldsList).Where(field => StaticHolders.IsHeld(field, type)).ToList();
        var heldProperties = (flattened?.Properties ?? (IReadOnlyList<Property>)type.PropertiesList).Where(property => StaticHolders.IsHeld(property, type)).ToList();
        var bodies = type.ConstructorsList.Where(StaticHolders.IsHeld).ToList();

        if(held.Count == 0 && heldProperties.Count == 0 && bodies.Count == 0)
        {
            return;
        }

        var slots = SlotRoutes.Carried(type);
        var parameters = string.Join(", ", SlotRoutes.HiddenParameters(slots));
        using var building = StaticHolders.Building(type);
        using var within = BodyEmitter.Within(type);
        using var construction = ConstructorWitnesses.NativeScope();
        var hosted = new List<StatementHost.Hosted>();

        TypeEmitter.SeparateMember(writer, ref anyEmitted);
        writer.WriteLine($"public sealed class {StaticHolders.Holder}");

        using(writer.Block())
        {
            foreach(var slot in slots)
            {
                writer.WriteLine($"public readonly {SlotRoutes.Keyword(slot)} {SlotRoutes.Hidden(slot)};");
            }

            foreach(var field in held)
            {
                EmitField(field, inReadonlyType, writer, diagnostics);
            }

            foreach(var property in heldProperties)
            {
                writer.WriteBlankLine();
                EmitProperty(property, writer, diagnostics);
            }

            var projected = true;
            SpreadProjectionEmitter.Emit(type, flattened, writer, ref projected, inHolder: true);

            writer.WriteBlankLine();
            writer.WriteLine($"public {StaticHolders.Holder}({parameters})");

            using(writer.Block())
            {
                foreach(var slot in slots)
                {
                    writer.WriteLine($"this.{SlotRoutes.Hidden(slot)} = {SlotRoutes.Hidden(slot)};");
                }

                writer.WriteLine($"{StaticHolders.Table}.Enter({StaticHolders.Key(slots)}, this);");

                foreach(var field in held)
                {
                    Initialize(IdentifierSpelling.Of(field.Name), field.Name, field.Initializer, field.Type, hosted, writer, diagnostics);
                }

                foreach(var property in heldProperties.Where(property => property.Initializer is not null || property.HasBackingStorage))
                {
                    Initialize(BackingStorage.DeclaredField(property) ?? IdentifierSpelling.Of(property.Name), property.Name, property.Initializer, property.Type, hosted, writer, diagnostics);
                }

                foreach(var constructor in bodies.Where(constructor => constructor.Body is not null))
                {
                    BodyEmitter.EmitMethodBody(constructor, constructor.Body!, writer, diagnostics, owner: type);
                }
            }

            StatementHost.EmitMethods(hosted, writer, diagnostics, instance: true);
        }

        var key = StaticHolders.KeyType(slots);
        var table = $"{StaticHolders.Runtime()}.Table<{key}, {StaticHolders.Holder}>";

        writer.WriteBlankLine();
        writer.WriteLine($"private static readonly {table} {StaticHolders.Table} = new {table}(static key => new {StaticHolders.Holder}({StaticHolders.Unpacked(slots, "key")}));");
        writer.WriteBlankLine();
        writer.WriteLine($"public static {StaticHolders.Holder} {StaticHolders.Lookup}({parameters})");

        using(writer.Block())
        {
            writer.WriteLine($"return {StaticHolders.Table}.Of({StaticHolders.Key(slots)});");
        }
    }

    public static void Touch(MemberType? type, CSharpWriter writer)
    {
        if(StaticHolders.HasStaticConstructor(type))
        {
            var routed = SlotRoutes.Carried(type);
            writer.WriteLine($"_ = {StaticHolders.Through(TypeNameEmitter.Emit(new ConstructedType(type!, type!.TypeParametersList)), SlotRoutes.HiddenNames(routed))};");
        }
    }

    private static void EmitField(Field field, bool inReadonlyType, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        AttributeEmitter.EmitLines(field.DeclaredAttributesList, writer, diagnostics);

        if(field.Initializer is null && field.DeclaringType is ClassType
            && !TypeNameEmitter.IsValueKind(field.Type)
            && Defaultability.IsUndecided(TypeNameEmitter.Closed(field.Type)))
        {
            writer.WriteLine("[System.Diagnostics.CodeAnalysis.AllowNull]");
        }

        var modifiers = MemberModifierEmitter.Emit(
            new MemberModifiers { Accessibility = StaticHolders.Reach(field.Modifiers.Accessibility) }, emitReadonly: field.Modifiers.IsReadonly || inReadonlyType);

        writer.WriteLine($"{modifiers}{TypeNameEmitter.Emit(field.Type)} {IdentifierSpelling.Of(field.Name)};");
    }

    private static void EmitProperty(Property property, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var reach = StaticHolders.Reach(property.Modifiers.Accessibility);
        var narrowed = reach == Accessibility.Public ? "internal " : "";
        var typeName = TypeNameEmitter.Emit(property.Type);
        var declaration = $"{MemberModifierEmitter.Spell(reach)}{typeName} {IdentifierSpelling.Of(property.Name)}";
        var storage = BackingStorage.DeclaredField(property);

        AttributeEmitter.EmitLines(property.DeclaredAttributesList, writer, diagnostics);

        if(storage is not null)
        {
            writer.WriteLine($"internal {typeName} {storage};");
        }

        if(AccessorEmitter.IsBodiless(property.Getter) && AccessorEmitter.IsBodiless(property.Setter))
        {
            var accessors = new List<string>(2);

            if(property.Getter is { } bodilessGetter)
            {
                accessors.Add(AccessorEmitter.EmitBodiless(bodilessGetter, isSetter: false, narrowed));
            }

            if(property.Setter is { } bodilessSetter)
            {
                accessors.Add(AccessorEmitter.EmitBodiless(bodilessSetter, isSetter: true, narrowed));
            }

            writer.WriteLine($"{declaration} {{ {string.Join(" ", accessors)} }}");
            return;
        }

        writer.WriteLine(declaration);

        using(writer.Block())
        {
            if(property.Getter is { } getter)
            {
                if(storage is not null && AccessorEmitter.IsBodiless(getter))
                {
                    AccessorEmitter.EmitOverStorage(getter, isSetter: false, isReadonly: false, storage, writer, narrowed);
                }
                else
                {
                    AccessorEmitter.Emit(getter, isSetter: false, isReadonly: false, writer, diagnostics, property.Type, narrowed);
                }
            }

            if(property.Setter is { } setter)
            {
                if(storage is not null && AccessorEmitter.IsBodiless(setter))
                {
                    AccessorEmitter.EmitOverStorage(setter, isSetter: true, isReadonly: false, storage, writer, narrowed);
                }
                else
                {
                    AccessorEmitter.Emit(setter, isSetter: true, isReadonly: false, writer, diagnostics, narrowed: narrowed);
                }
            }
        }
    }

    private static void Initialize(
        string target, string name, ProgramModel.Model.Expressions.Expression? initializer, ProgramModel.Contracts.Types.IMistType type,
        List<StatementHost.Hosted> hosted, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        if(initializer is null)
        {
            if(StructStamp.Of(type, diagnostics) is { } stamp)
            {
                writer.WriteLine($"this.{target} = {stamp};");
            }

            return;
        }

        writer.Write($"this.{target} = ");

        if(StatementHost.Emit(initializer, $"__Initialize{name}", type, [], writer, diagnostics) is { } host)
        {
            hosted.Add(host);
        }

        writer.Append(";");
        writer.AppendLine();
    }
}