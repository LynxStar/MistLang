using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// The readonly value parameter a receiver is reached through, however many instance fields and properties
/// deep: what a caller lends through a readonly parameter includes what its fields reach, so readonly-contexts
/// Requirements 14.2-14.4 hold for <c>item.Nested.Bump()</c> as they do for <c>item.Bump()</c>. Only the
/// syntactic chain is followed. A local copied from one, a call's result or an element is not, because
/// tracking where such an alias flows is an escape analysis this rule deliberately does not make.
/// </summary>
internal static class ReadonlyParameterReceivers
{
    public static LocalReferenceExpression? RootOf(Expression receiver)
    {
        var current = receiver;

        while(current is MemberAccessExpression { Member: FieldResult { Field.Modifiers.IsStatic: false } or PropertyResult { Property.Modifiers.IsStatic: false } } access)
        {
            current = access.Receiver;
        }

        return current is LocalReferenceExpression { Local.Parameter.Modifier.IsReadonly: true } parameter ? parameter : null;
    }

    public static string Describe(Expression receiver, LocalReferenceExpression root)
    {
        var parameter = $"readonly parameter '{root.Local.Name}'";

        return ReferenceEquals(receiver, root) ? parameter : $"'{Path(receiver)}', reached through {parameter}";
    }

    private static string Path(Expression receiver)
    {
        return receiver is MemberAccessExpression access
            ? $"{Path(access.Receiver)}.{access.MemberName}"
            : ((LocalReferenceExpression)receiver).Local.Name;
    }
}