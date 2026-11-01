using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramModel;

/// <summary>
/// Constant values known from projected CLR literal fields or Standard's intrinsic bounds. A Mist
/// storage initializer runs at runtime and does not establish the value of a later field read.
/// </summary>
internal static class ClrConstant
{
    public static LiteralExpression? Of(Expression? expression)
    {
        return expression switch
        {
            MemberAccessExpression { IsConditional: false, IsFailed: false, Member: FieldResult { Field: var field } } => Stored(field) ?? IntrinsicBounds.Of(field, expression.Node),
            FieldReferenceExpression { Field: var field } => Stored(field) ?? IntrinsicBounds.Of(field, expression.Node),
            _ => null,
        };
    }

    private static LiteralExpression? Stored(Field field)
    {
        return field is { Modifiers.IsClrProjected: true, Initializer: LiteralExpression literal } ? literal : null;
    }
}