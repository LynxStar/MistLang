using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Flow;

internal sealed record LocalStoragePath(LocalVariable Local, LocalStoragePath? Receiver = null, Field? Field = null)
{
    public string Name => Receiver is null ? Local.Name : $"{Receiver.Name}.{Field!.Name}";

    public static LocalStoragePath? Of(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression local => new LocalStoragePath(local.Local),
            SpreadExpression spread => Of(spread.Operand),
            MemberAccessExpression { IsConditional: false, Member: FieldResult { Field.Modifiers.IsStatic: false } field } access
                when IsValueType(access.Receiver.Type) && Of(access.Receiver) is { } receiver
                => new LocalStoragePath(receiver.Local, receiver, field.Field),
            _ => null,
        };
    }

    private static bool IsValueType(IMistType type)
    {
        return type is StructType or ConstructedType { Definition: StructType };
    }
}