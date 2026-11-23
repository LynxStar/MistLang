using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// What a reference a call returns may name besides what the call is handed by reference, which
/// <see cref="BodyHolders.Places"/> names. A field the callee returns names that field of whichever object it reads,
/// which may be the caller's <c>this</c>. A callee whose body cannot be read, because it has none or another type
/// may override it, may return any field or static of the reference's type.
/// </summary>
internal static class ReturnedStorage
{
    public static WriteReach Of(Expression call, Func<Expression, bool> distinct)
    {
        return Of(call, new HashSet<Method>(ReferenceEqualityComparer.Instance), distinct);
    }

    private static WriteReach Of(Expression call, HashSet<Method> reading, Func<Expression, bool> distinct)
    {
        if(call is not InvocationExpression { Method: { } selected } invocation)
        {
            return WriteReach.Storage(call.Type, anyField: true);
        }

        var method = selected.SourceDeclaration ?? selected;
        var unreadable = (method.DeclaringType is AbstractType && !method.Modifiers.IsStatic) || method.Body is null;
        if(unreadable || !reading.Add(method))
        {
            return WriteReach.Storage(call.Type, anyField: true);
        }

        var reach = WriteReach.Storage(call.Type);
        foreach(var returned in BodyWalker.DescendantsAndSelf(method.Body!).OfType<ReturnStatement>().Where(statement => statement.IsRef))
        {
            reach = reach.Or(returned.Value is { } value ? Named(value, invocation, method, reading, distinct) : WriteReach.Storage(call.Type, anyField: true));
        }

        _ = reading.Remove(method);
        return reach;
    }

    private static WriteReach Named(Expression returned, InvocationExpression call, Method method, HashSet<Method> reading, Func<Expression, bool> distinct)
    {
        return returned switch
        {
            FieldReferenceExpression { Field: { Modifiers.IsStatic: false } field } => Owned(field, ThisTiers.ReceiverOf(call), distinct),
            FieldReferenceExpression field => WriteReach.Field(field.Field),
            MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: ThisExpression } => Owned(field, ThisTiers.ReceiverOf(call), distinct),
            MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: LocalReferenceExpression { Local.Parameter: { Modifier.IsRef: false } parameter } } =>
                Owned(field, Argument(call, method, parameter), distinct),
            MemberAccessExpression { Member: FieldResult field } => WriteReach.Field(field.Field),
            LocalReferenceExpression { Local.Parameter.Modifier.IsRef: true } => WriteReach.Nothing,
            IndexExpression { Indexer: null } element => WriteReach.Storage(element.Type),
            InvocationExpression nested when BodyHolders.ReturnsRef(nested) => BodyHolders.Places(nested).Aggregate(Of(nested, reading, _ => false),
                (reach, place) => reach.Or(Named(place, call, method, reading, distinct))),
            _ => WriteReach.Storage(returned.Type, anyField: true),
        };
    }

    private static WriteReach Owned(Field field, Expression? receiver, Func<Expression, bool> distinct)
    {
        return receiver is not null && distinct(receiver) ? WriteReach.Storage(field.Type) : WriteReach.Field(field);
    }

    private static Expression? Argument(InvocationExpression call, Method method, Parameter parameter)
    {
        var index = method.ParametersList.IndexOf(parameter);
        return index >= 0 && index < call.Arguments.Count ? call.Arguments[index] : null;
    }
}