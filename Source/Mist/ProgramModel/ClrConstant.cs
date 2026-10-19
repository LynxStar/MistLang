using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel;

/// <summary>
/// The value metadata records for a CLR literal field, read where an expression names one. Only a
/// projected field qualifies: a Mist field's initializer is evaluated at run time and says nothing
/// about what the field holds when it is read.
/// </summary>
internal static class ClrConstant
{
    public static LiteralExpression? Of(Expression? expression)
    {
        return expression switch
        {
            MemberAccessExpression { IsConditional: false, IsFailed: false, Member: FieldResult { Field: var field } } => Stored(field),
            FieldReferenceExpression { Field: var field } => Stored(field),
            _ => null,
        };
    }

    private static LiteralExpression? Stored(Field field)
    {
        return field is { Modifiers.IsClrProjected: true, Initializer: LiteralExpression literal } ? literal : null;
    }
}