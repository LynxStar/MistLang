using Mist.ProgramModel.Construction.Binding;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction;

internal static class ValueStorage
{
    public static StorageLayout Describe(IMistType type)
    {
        if(type is NamedTupleType named)
        {
            type = named.Underlying;
        }

        if(type is TupleType tuple)
        {
            return TupleLayout(tuple);
        }

        var receiver = type as ConstructedType;
        if((receiver?.Definition ?? type) is not StructType definition)
        {
            return new StorageLayout(false, []);
        }

        var substitution = TypeSubstitution.ForReceiver(receiver);
        if(definition.ImportedStorage is { } imported)
        {
            return new StorageLayout(imported.IsComplete,
                imported.Fields.Select(field => field with { Type = substitution.Apply(field.Type, interner: null) }).ToArray(),
                imported.BaseType is { } inherited ? substitution.Apply(inherited, interner: null) : null);
        }

        var fields = definition.FieldsList.Where(field => !field.Modifiers.IsStatic)
            .Select(field => new StorageField(field.Name, substitution.Apply(field.Type, interner: null), field))
            .Concat(definition.PropertiesList.Where(property => !property.Modifiers.IsStatic && property.HasBackingStorage)
                .Select(property => new StorageField(property.Name, substitution.Apply(property.Type, interner: null))))
            .ToArray();
        var complete = !definition.IsIntrinsic && definition.ClrMetadataName is null && !definition.BaseUnresolved;
        return new StorageLayout(complete, fields,
            definition.BaseType is { } baseType ? substitution.Apply(baseType, interner: null) : null);
    }

    private static StorageLayout TupleLayout(TupleType tuple)
    {
        var fields = new List<StorageField>();
        foreach(var field in tuple.PositionalFields)
        {
            if(field.Type is SpreadType { Operand: TypeParameter { PackLength: { } length } parameter })
            {
                for(var i = 0; i < length; i++)
                {
                    fields.Add(new StorageField(TupleType.PositionalName(fields.Count + 1), parameter.ElementAt(i)));
                }
            }
            else if(field.Type is SpreadType)
            {
                return new StorageLayout(false, fields);
            }
            else
            {
                fields.Add(new StorageField(TupleType.PositionalName(fields.Count + 1), field.Type, field));
            }
        }

        return new StorageLayout(true, fields);
    }
}