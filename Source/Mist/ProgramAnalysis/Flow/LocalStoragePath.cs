using Mist.ProgramModel.Construction;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Flow;

internal sealed record LocalStoragePath(LocalVariable Local, LocalStoragePath? Receiver = null, Field? Field = null, int? ElementIndex = null)
{
    public string Name => Receiver is null ? Local.Name : $"{Receiver.Name}.{Field?.Name ?? TupleType.PositionalName(ElementIndex!.Value + 1)}";

    public object? Key => (object?)Field ?? ElementIndex;

    public static LocalStoragePath? Of(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression local => new LocalStoragePath(local.Local),
            SpreadExpression spread => Of(spread.Operand),
            MemberAccessExpression { IsConditional: false, Member: FieldResult { Field.Modifiers.IsStatic: false } field } access
                when IsValueType(access.Receiver.Type) && Of(access.Receiver) is { } receiver
                => Member(receiver, access.Receiver.Type, field.Field),
            _ => null,
        };
    }

    private static LocalStoragePath Member(LocalStoragePath receiver, IMistType type, Field field)
    {
        if(field.StorageSource is { } aggregate && field.StorageElementIndex is { } projection)
        {
            return new LocalStoragePath(receiver.Local, Member(receiver, type, aggregate), ElementIndex: projection);
        }

        var tuple = type is NamedTupleType named ? named.Underlying : type as TupleType;
        if(tuple is not null)
        {
            var canonical = tuple.PositionalFields.FirstOrDefault(candidate => ReferenceEquals(candidate, field));
            for(var index = 0; canonical is null && index < tuple.PositionalFields.Count; index++)
            {
                if(field.Name == TupleType.PositionalName(index + 1)
                    || (type is NamedTupleType aliases && index < aliases.ElementNames.Count && aliases.ElementNames[index] == field.Name))
                {
                    canonical = tuple.PositionalFields[index];
                }
            }

            var layout = ValueStorage.Describe(type);
            for(var index = 0; canonical is not null && index < layout.Fields.Count; index++)
            {
                if(ReferenceEquals(layout.Fields[index].Field, canonical))
                {
                    return new LocalStoragePath(receiver.Local, receiver, ElementIndex: index);
                }
            }
        }

        return new LocalStoragePath(receiver.Local, receiver, field);
    }

    private static bool IsValueType(IMistType type)
    {
        return type is StructType or ConstructedType { Definition: StructType } or ITupleType;
    }
}