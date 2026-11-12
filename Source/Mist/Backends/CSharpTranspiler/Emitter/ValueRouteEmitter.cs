using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The members a value-routed declaration carries its slot values in: a class or struct keeps each in
/// a public readonly field set by every constructor, an abstract declares a getter, and an implementing
/// type answers each abstract it names explicitly, so a type implementing two abstracts whose slots
/// share a name answers each on its own. A struct answers each absorbed ancestor's slot as a property
/// over its own, since flattening leaves it no base to inherit the field from. The field is public
/// because a test or a read reaches it through any receiver, and readonly because an object's type is
/// fixed when it is created. Two overloads coincide when the parameter lists C# sees are the same, spelled
/// here in the order <see cref="MethodEmitter"/> and <see cref="ExtensionBlockEmitter"/> write them: the
/// receiver an extension method takes, the hidden slot parameters, the witnesses and the declared parameters,
/// each as its type alone, under the count of type parameters the method keeps.
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

        foreach(var (ancestor, _) in type is StructType flattened ? DefaultSynthesis.FlattenedAncestors(flattened) : [])
        {
            var closing = SlotRoutes.ClosingFrom(type, ancestor);

            foreach(var slot in SlotRoutes.ValueRouted(ancestor))
            {
                var argument = closing is not null && slot.Ordinal < closing.TypeArguments.Count ? closing.TypeArguments[slot.Ordinal] : null;
                var value = argument is null ? null : BodyEmitter.SlotValue(argument, diagnostics);

                writer.WriteLine($"public {SlotRoutes.Keyword(slot)} {SlotRoutes.Hidden(slot)} => "
                    + $"{value ?? EmissionDiagnostics.Fallback("slot value", $"{TypeEmitter.DeclaredName(type)}.{slot.Name}", "default")};");
                written = true;
            }
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

        if(type is ClassType && type.BaseType is { } baseType)
        {
            ConstructorEmitter.EmitImplicitChain(type, baseType, writer, diagnostics);
        }

        writer.AppendLine();
        using(writer.Block())
        {
            EmitAssignments(type, writer);
        }
    }

    public static IReadOnlyList<ConstructorWitness> ClosableConstraints(
        MemberType type, IReadOnlyList<ConstructorWitness> closedConstraints, DiagnosticBag diagnostics)
    {
        if(closedConstraints.Count == 0 || SlotRoutes.ValueRouted(type) is not [var first, ..] || type.DeclarationNode is not { } node)
        {
            return closedConstraints;
        }

        foreach(var witness in closedConstraints)
        {
            diagnostics.ReportAtNodeTyped(
                BackendDiagnostics.ValueRoutedConstructorConstraintClosure, node, null,
                DiagnosticSnapshot.Type(type, TypeEmitter.DeclaredName(type)), new NameValue(first.Name),
                new SequenceValue(SlotKind.Type, [.. witness.Signature.Select(DiagnosticSnapshot.Type)]));
        }

        return [];
    }

    public static void ReportCoincidingOverloads(IEnumerable<Method> methods, DiagnosticBag diagnostics)
    {
        foreach(var group in methods.Where(method => method.DeclarationNode is not null).GroupBy(method => method.Name, StringComparer.Ordinal))
        {
            var declared = group.ToList();

            if(declared.Count < 2)
            {
                continue;
            }

            var spelled = declared.ConvertAll(Spelled);

            for(var later = 1; later < declared.Count; later++)
            {
                var earlier = spelled.FindIndex(0, later, spelling => spelling == spelled[later]);

                if(earlier < 0 || ErasedDifference(declared[earlier], declared[later]) is not var (slot, type))
                {
                    continue;
                }

                diagnostics.ReportAtNodeTyped(
                    BackendDiagnostics.CoincidingOverloads, declared[later].DeclarationNode!, null,
                    new NameValue(group.Key), new NameValue(Display(declared[earlier])), new NameValue(slot.Name), type);
            }
        }
    }

    private static string Spelled(Method method)
    {
        var block = method.DeclaringBlock;
        var declared = method.TypeParametersList;
        var carried = block is null ? declared : [.. block.TypeParametersList, .. declared];
        var parts = new List<string>();

        if(block is { ResolvedReceiverType: { } receiver } && !method.Modifiers.IsStatic)
        {
            parts.Add(TypeNameEmitter.Emit(receiver));
        }

        parts.AddRange(SlotRoutes.ValueRouted(method is { Modifiers.IsStatic: true, DeclaringType: MemberType declaring } ? declaring : null).Select(SlotRoutes.Keyword));
        parts.AddRange(SlotRoutes.ValueRouted(carried).Select(SlotRoutes.Keyword));
        parts.AddRange(DefaultWitnesses.StaticParameters(method).Select(DefaultConstraintWitness.Parameter));
        parts.AddRange(DefaultConstraintWitness.Parameters(declared));
        parts.AddRange(method is { Modifiers.IsStatic: true, DeclaringType: ConcreteType owner } ? ConstructorWitnesses.Parameters(ConstructorWitnesses.Stored(owner)) : []);
        parts.AddRange(ConstructorWitnesses.Parameters(ConstructorWitnesses.Of(declared)));
        parts.AddRange(ContextKeyWitnesses.Parameters(method));
        parts.AddRange(method.ParametersList.Select(parameter =>
            RefKeywords.For(parameter.Modifier.IsRef, parameter.Modifier.IsReadonly) + ParameterEmitter.SpelledType(parameter, NativeSlotNullability.Declares(parameter))));

        return $"{carried.Count(parameter => !SlotRoutes.IsValueRouted(parameter))}:{string.Join(",", parts)}";
    }

    private static (TypeParameter Slot, TypeValue Type)? ErasedDifference(Method earlier, Method later)
    {
        for(var i = 0; i < earlier.ParametersList.Count && i < later.ParametersList.Count; i++)
        {
            if(Erased(earlier.ParametersList[i].Type, later.ParametersList[i].Type) is var (slot, type))
            {
                return (slot, DiagnosticSnapshot.Type(type));
            }
        }

        foreach(var method in new[] { later, earlier })
        {
            var own = method.DeclaringBlock is { } block ? [.. block.TypeParametersList, .. method.TypeParametersList] : method.TypeParametersList;

            if(SlotRoutes.ValueRouted(own) is not [var slot, ..])
            {
                continue;
            }

            var carrier = method.ParametersList.Select(parameter => Carrying(parameter.Type, slot)).FirstOrDefault(found => found is not null);

            return (slot, carrier is null
                ? DiagnosticSnapshot.Type(slot, $"{method.Name}<{string.Join(", ", own.Select(parameter => parameter.Name))}>")
                : DiagnosticSnapshot.Type(carrier));
        }

        return null;
    }

    private static (TypeParameter Slot, ConstructedType Type)? Erased(IMistType earlier, IMistType later)
    {
        if(TypeNameEmitter.Closed(earlier) is not ConstructedType first
            || TypeNameEmitter.Closed(later) is not ConstructedType second
            || !ReferenceEquals(first.Definition, second.Definition)
            || first.Definition is not IHasTypeParameters generic)
        {
            return null;
        }

        foreach(var slot in generic.TypeParametersList)
        {
            if(slot.Ordinal >= first.TypeArguments.Count || slot.Ordinal >= second.TypeArguments.Count)
            {
                continue;
            }

            var (one, other) = (first.TypeArguments[slot.Ordinal], second.TypeArguments[slot.Ordinal]);

            if(slot.IsDependent)
            {
                if(!ReferenceEquals(one, other) && (SlotRoutes.IsValueRouted(slot) || LengthErasure.IsLengthIndexed(first)))
                {
                    return (slot, first);
                }
            }
            else if(Erased(one, other) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static ConstructedType? Carrying(IMistType type, TypeParameter slot)
    {
        if(TypeNameEmitter.Closed(type) is not ConstructedType constructed)
        {
            return null;
        }

        foreach(var argument in constructed.TypeArguments)
        {
            if(ReferenceEquals(argument, slot))
            {
                return constructed;
            }

            if(Carrying(argument, slot) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    private static string Display(Method method)
    {
        var typeParameters = method.TypeParametersList.Count == 0 ? "" : $"<{string.Join(", ", method.TypeParametersList.Select(parameter => parameter.Name))}>";

        return $"{method.Name}{typeParameters}({string.Join(", ", method.ParametersList.Select(parameter => TypeDisplay.Mist(parameter.Type)))})";
    }

    public static void Report(MemberType type, DiagnosticBag diagnostics)
    {
        var node = type.DeclarationNode;

        if(node is null)
        {
            return;
        }

        ReportCoincidingOverloads(type.MethodsList, diagnostics);

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