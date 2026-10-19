using Mist.Backends.CSharpTranspiler.Lowering;
using Mist.ProgramModel.Construction.Members;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.Backends.CSharpTranspiler.Emitter;

/// <summary>
/// The per-element members a spread declaration stands for: <c>public TArgs... Item;</c> exposes
/// the aggregate <c>Item</c> and one projection <c>Item1</c> .. <c>ItemN</c>.
/// <para>
/// PROJECTED RATHER THAN STORED, whichever kind the declaration used. The aggregate holds the
/// values and each element reads through it, so writing one is writing the other -- which is what
/// the declaration means and what independent backing fields would quietly break, since the
/// constructor fills only the aggregate. C# names a tuple's elements Item1..ItemN whatever the
/// aggregate is called, and does so at every width: the zero and one cases emit as ValueTuple
/// rather than as tuple syntax, which has those members too.
/// </para>
/// </summary>
internal static class SpreadProjectionEmitter
{
    public static void Emit(MemberType type, FlattenedStruct? flattened, CSharpWriter writer, ref bool anyEmitted)
    {
        var fields = flattened is not null
            ? flattened.Fields.Select(field => (field.Field, field.Type, field.Field.Modifiers with { Accessibility = field.Accessibility }))
            : ((type as ConcreteType)?.FieldsList ?? []).Select(field => (field, field.Type, field.Modifiers));

        foreach(var (field, held, modifiers) in fields)
        {
            EmitFamily(field.Name, field.Type, held, modifiers, item => $"set => {IdentifierSpelling.Of(field.Name)}.{item} = value;", writer, ref anyEmitted);
        }

        foreach(var property in flattened?.Properties ?? (IReadOnlyList<Property>)type.PropertiesList)
        {
            using var inherited = TypeEmitter.Substituting(flattened?.PropertyPaths.GetValueOrDefault(property));
            EmitFamily(
                property.Name, property.Type, property.Type, property.Modifiers, WriteBack(property), writer, ref anyEmitted);
        }
    }

    /// <summary>
    /// A property hands back a COPY of the tuple, so an element cannot be assigned through it the way a
    /// field's can: the setter reads the aggregate, changes the one element and stores it back, at the
    /// setter's own accessibility -- private for the setter a flattened struct gives an inherited
    /// get-only property.
    /// </summary>
    private static Func<string, string>? WriteBack(Property property)
    {
        if(property.Setter is not { } setter)
        {
            return null;
        }

        var accessibility = setter.Accessibility == Accessibility.Private ? "private " : "";

        return item => $"{accessibility}set {{ var aggregate = {IdentifierSpelling.Of(property.Name)}; aggregate.{item} = value; {IdentifierSpelling.Of(property.Name)} = aggregate; }}";
    }

    private static void EmitFamily(
        string stem,
        IMistType declaredType,
        IMistType held,
        MemberModifiers modifiers,
        Func<string, string>? write,
        CSharpWriter writer,
        ref bool anyEmitted)
    {
        if(!SpreadMember.IsSpread(declaredType) || Elements(held) is not { } expansion)
        {
            return;
        }

        var emitted = MemberModifierEmitter.Emit(modifiers, emitReadonly: false);

        for(var i = 0; i < expansion.Count; i++)
        {
            var element = TypeNameEmitter.Emit(expansion[i]);
            var item = $"Item{i + 1}";
            var read = $"get => {IdentifierSpelling.Of(stem)}.{item};";
            var accessors = write is null ? read : $"{read} {write(item)}";

            TypeEmitter.SeparateMember(writer, ref anyEmitted);
            writer.WriteLine($"{emitted}{element} {stem}{i + 1} {{ {accessors} }}");
        }
    }

    private static List<IMistType>? Elements(IMistType held)
    {
        if(TypeNameEmitter.Closed(held) is not TupleType tuple)
        {
            return null;
        }

        var elements = new List<IMistType>();

        foreach(var element in tuple.ElementTypes)
        {
            if(element is not SpreadType spread)
            {
                elements.Add(element);
                continue;
            }

            if(MonomorphReference.Elements(TypeNameEmitter.Closed(spread.Operand)) is not { } spliced)
            {
                return null;
            }

            elements.AddRange(spliced);
        }

        return elements;
    }
}