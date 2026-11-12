using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The members a value-routed declaration carries its slot values in: a class or struct keeps each in
/// a public readonly field set by every constructor, an abstract declares a getter, and an implementing
/// type answers each abstract it names explicitly, so a type implementing two abstracts whose slots
/// share a name answers each on its own. The field is public because a test or a read reaches it
/// through any receiver, and readonly because an object's type is fixed when it is created.
/// </summary>
internal static class ValueRouteEmitter
{
    public static bool EmitMembers(MemberType type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        var written = false;

        foreach(var slot in SlotRoutes.ValueRouted(type))
        {
            writer.WriteLine(type is AbstractType
                ? $"{SlotRoutes.Keyword(slot)} {SlotRoutes.Hidden(slot)} {{ get; }}"
                : $"public readonly {SlotRoutes.Keyword(slot)} {SlotRoutes.Hidden(slot)};");
            written = true;
        }

        foreach(var closing in Answered(type))
        {
            foreach(var slot in SlotRoutes.ValueRouted(closing.Definition))
            {
                var argument = slot.Ordinal < closing.TypeArguments.Count ? closing.TypeArguments[slot.Ordinal] : null;
                var value = argument is null ? null : BodyEmitter.SlotValue(argument, diagnostics);

                writer.WriteLine(
                    $"{SlotRoutes.Keyword(slot)} {TypeNameEmitter.Emit(closing)}.{SlotRoutes.Hidden(slot)} => "
                    + $"{value ?? EmissionDiagnostics.Fallback("slot value", $"{TypeDisplay.Mist(closing)}.{slot.Name}", "default")};");
                written = true;
            }
        }

        return written;
    }

    private static IEnumerable<ConstructedType> Answered(MemberType type)
    {
        var reached = type is AbstractType abstractType ? abstractType.BaseAbstractTypes : TypeSlots.Reached(type);

        return reached.OfType<ConstructedType>().Where(closing => SlotRoutes.ValueRouted(closing.Definition).Count > 0);
    }

    public static void EmitAssignments(ConcreteType type, CSharpWriter writer)
    {
        foreach(var slot in SlotRoutes.ValueRouted(type))
        {
            writer.WriteLine($"this.{SlotRoutes.Hidden(slot)} = {SlotRoutes.Hidden(slot)};");
        }
    }

    public static bool NeedsConstructor(ConcreteType type)
    {
        return SlotRoutes.ValueRouted(type).Count > 0
            && !type.ConstructorsList.Exists(constructor => !constructor.Modifiers.IsStatic)
            && !DefaultSynthesis.IsDefaultable(type)
            && (DefaultSynthesis.NeedsSentinel(type) || DefaultWitnesses.FactoryParameters(type).Count == 0);
    }

    public static void EmitConstructor(ConcreteType type, CSharpWriter writer, DiagnosticBag diagnostics)
    {
        writer.Write($"public {TypeEmitter.CleanName(type)}({string.Join(", ", SlotRoutes.HiddenParameters(SlotRoutes.ValueRouted(type)))})");

        if(type.BaseType is { } baseType)
        {
            ConstructorEmitter.EmitImplicitChain(type, baseType, writer, diagnostics);
        }

        writer.AppendLine();
        using(writer.Block())
        {
            EmitAssignments(type, writer);
        }
    }

    public static void Report(MemberType type, DiagnosticBag diagnostics)
    {
        var node = type.DeclarationNode;

        if(node is null)
        {
            return;
        }

        if(SlotRoutes.ValueRouted(type) is [var first, ..] && type is ConcreteType concrete)
        {
            foreach(var field in concrete.FieldsList.Where(field => field.Modifiers.IsStatic))
            {
                diagnostics.ReportAtNodeTyped(
                    BackendDiagnostics.StaticStorageOnValueRoute, field.DeclarationNode ?? node, null,
                    new NameValue(field.Name), DiagnosticSnapshot.Type(type, TypeEmitter.DeclaredName(type)), new NameValue(first.Name));
            }

            foreach(var property in type.PropertiesList.Where(property => property.Modifiers.IsStatic))
            {
                diagnostics.ReportAtNodeTyped(
                    BackendDiagnostics.StaticStorageOnValueRoute, property.DeclarationNode ?? node, null,
                    new NameValue(property.Name), DiagnosticSnapshot.Type(type, TypeEmitter.DeclaredName(type)), new NameValue(first.Name));
            }
        }

        foreach(var group in Answered(type).GroupBy(closing => closing.Definition))
        {
            var closings = group.ToList();

            foreach(var slot in SlotRoutes.ValueRouted(group.Key))
            {
                var values = closings
                    .Select(closing => slot.Ordinal < closing.TypeArguments.Count ? closing.TypeArguments[slot.Ordinal] : null)
                    .Distinct(ReferenceEqualityComparer.Instance)
                    .ToList();

                if(values.Count > 1)
                {
                    diagnostics.ReportAtNodeTyped(
                        BackendDiagnostics.ValueRoutedAbstractAtTwoValues, node, null,
                        DiagnosticSnapshot.Type(type, TypeEmitter.DeclaredName(type)), DiagnosticSnapshot.Type(group.Key), new NameValue(slot.Name));
                }
            }
        }
    }
}