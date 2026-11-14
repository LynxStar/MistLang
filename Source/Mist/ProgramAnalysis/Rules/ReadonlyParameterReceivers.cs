using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// A readonly parameter lends its reachable fields too (readonly-contexts Requirements 14.2-14.4).
/// Follow only the syntactic member chain; copied locals, call results and elements require alias-flow
/// analysis, which this rule deliberately omits.
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