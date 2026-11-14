using Mist.Backends.CSharpTranspiler.Emitter;
using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Lowering;

/// <summary>
/// The value every element of an array creation or a stack allocation starts from when the element holds a
/// value-routed struct by value, at any depth: the type's own default where the backend synthesizes one, so a
/// declared default is honoured as definite-assignment Requirement 4.1b asks, and otherwise the struct built
/// from its parts, each part the CLR zero it would have had with the hidden slot fields filled in. Built once
/// where the storage is made, with the closing written out in full, so no description of a type parameter is
/// needed; the element's hidden fields are what make its stamp differ from the CLR zero the storage would
/// otherwise keep.
/// </summary>
internal static class StructStamp
{
    public static string? Of(IMistType element, DiagnosticBag diagnostics)
    {
        var closed = BodyEmitter.Unaliased(TypeNameEmitter.Closed(element));

        return HeldByValue(closed, new HashSet<IMistType>(ReferenceEqualityComparer.Instance)) is null ? null : Build(closed, diagnostics);
    }

    public static ConstructedType? HeldByValue(IMistType type)
    {
        return HeldByValue(type, new HashSet<IMistType>(ReferenceEqualityComparer.Instance));
    }

    private static string Build(IMistType type, DiagnosticBag diagnostics)
    {
        return type switch
        {
            TupleType tuple => $"({string.Join(", ", tuple.ElementTypes.Select(element => Part(element, diagnostics)))})",
            NamedTupleType named => $"({string.Join(", ", named.ElementTypes.Select(element => Part(element, diagnostics)))})",
            _ => DefaultSynthesis.Expression(type) ?? FromParts(type, diagnostics),
        };
    }

    private static string Part(IMistType member, DiagnosticBag diagnostics)
    {
        var closed = BodyEmitter.Unaliased(TypeNameEmitter.Closed(member));

        return HeldByValue(closed) is not null
            ? Build(closed, diagnostics)
            : DefaultSynthesis.StorageZero(closed) ?? $"default({TypeNameEmitter.Emit(closed)})";
    }

    private static string FromParts(IMistType type, DiagnosticBag diagnostics)
    {
        if(((type as ConstructedType)?.Definition ?? type) is not StructType definition)
        {
            return EmissionDiagnostics.Fallback("struct stamp", TypeDisplay.Mist(type), "default");
        }

        var flattened = new StructFlattener(diagnostics).Flatten(definition);
        var closing = TypeSubstitution.ForReceiver(type as ConstructedType);
        var arguments = BodyEmitter.SlotValues(type as ConstructedType, diagnostics);
        arguments.AddRange(DefaultWitnesses.Arguments(DefaultWitnesses.Stored(definition), type) ?? []);
        arguments.AddRange(ConstructorWitnesses.Arguments(ConstructorWitnesses.Stored(definition), type));

        foreach(var member in StructAncestorFactory.MemberTypes(flattened))
        {
            arguments.Add(Part(closing.Apply(member, interner: null), diagnostics));
        }

        arguments.Add($"default({StructAncestorFactory.MarkerType})");

        return $"{TypeNameEmitter.Emit(type)}.{StructAncestorFactory.Name(flattened)}({string.Join(", ", arguments)})";
    }

    private static ConstructedType? HeldByValue(IMistType type, HashSet<IMistType> seen)
    {
        if(!seen.Add(type))
        {
            return null;
        }

        return BodyEmitter.Unaliased(type) switch
        {
            ConstructedType { Definition: StructType definition } constructed => SlotRoutes.ValueRouted(definition).Count > 0
                ? constructed
                : HeldByValue(definition, TypeSubstitution.ForReceiver(constructed), seen),
            StructType definition => HeldByValue(definition, TypeSubstitution.Empty, seen),
            TupleType tuple => tuple.ElementTypes.Select(element => HeldByValue(element, seen)).FirstOrDefault(found => found is not null),
            NamedTupleType named => named.ElementTypes.Select(element => HeldByValue(element, seen)).FirstOrDefault(found => found is not null),
            _ => null,
        };
    }

    private static ConstructedType? HeldByValue(StructType definition, TypeSubstitution closing, HashSet<IMistType> seen)
    {
        foreach(var field in definition.FieldsList)
        {
            if(!field.Modifiers.IsStatic && HeldByValue(closing.Apply(field.Type, interner: null), seen) is { } inField)
            {
                return inField;
            }
        }

        foreach(var property in definition.PropertiesList)
        {
            if(property.Modifiers is { IsStatic: false } && property.HasBackingStorage && HeldByValue(closing.Apply(property.Type, interner: null), seen) is { } inProperty)
            {
                return inProperty;
            }
        }

        return null;
    }
}