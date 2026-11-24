using System.Collections.Immutable;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

/// <summary>
/// The storage a ref local or a ref-returning call stands for, in the caller's terms. A ref local stands for
/// its declaration's initializer. A call stands for each place its callee's <c>return ref</c> statements name,
/// with the callee's <c>this</c> read as the call's receiver and a parameter the callee never assigns read as its
/// argument, through nested ref locals and calls; a place under the caller's own <c>this</c> has no root. A callee
/// with no body here, or storage the caller cannot name, contributes nothing.
/// </summary>
internal static class RefAliases
{
    public static IEnumerable<Aliased> Of(Expression expression, object? root)
    {
        return Of(expression, local => Referent(local, root));
    }

    public static IEnumerable<Aliased> Of(Expression expression, Func<LocalVariable, Expression?> referent)
    {
        return Places(expression, new Frame(referent, null, null, null, null), []);
    }

    private static IEnumerable<Aliased> Places(Expression expression, Frame frame, ImmutableHashSet<object> visiting)
    {
        var place = OwnershipAnalyzer.Unwrapped(expression);

        if(place is LocalReferenceExpression { Local: { IsRef: true, Parameter: null } alias })
        {
            return !visiting.Contains(alias) && frame.Referent(alias) is { } referent ? Places(referent, frame, visiting.Add(alias)) : Named(place, frame, visiting);
        }

        if(place is InvocationExpression { Signature: { Modifiers.IsRef: true } signature } call)
        {
            return Returned(call, signature.SourceDeclaration ?? signature, frame, visiting);
        }

        return Named(place, frame, visiting);
    }

    private static IEnumerable<Aliased> Named(Expression place, Frame frame, ImmutableHashSet<object> visiting)
    {
        if(frame.Call is null)
        {
            return [new Aliased(place, [])];
        }

        return place switch
        {
            ThisExpression when frame.Self is { } self => [self],
            FieldReferenceExpression { Field: { Modifiers.IsStatic: false } field } when frame.Self is { } self => [self.Child(field)],
            MemberAccessExpression { Member: FieldResult { Field: { Modifiers.IsStatic: false } field }, Receiver: var receiver } and not { Receiver: TypeReferenceExpression }
                => Places(receiver, frame, visiting).Select(holder => holder.Child(field)),
            LocalReferenceExpression { Local.Parameter: { IsFlattened: false } parameter } when Argument(frame, parameter) is { } argument
                => Places(argument, frame.Caller!, visiting),
            _ => [],
        };
    }

    private static IEnumerable<Aliased> Returned(InvocationExpression call, Method method, Frame frame, ImmutableHashSet<object> visiting)
    {
        if(method.Body is not { } body || visiting.Contains(method))
        {
            return [];
        }

        var receiver = ThisTiers.ReceiverOf(call);
        List<Aliased?> selves = method.Modifiers.IsStatic ? [null]
            : receiver is not null ? [.. Places(receiver, frame, visiting)]
            : frame.Call is null ? [new Aliased(null, [])]
            : [frame.Self];
        var inside = visiting.Add(method);
        var returned = BodyWalker.DescendantsAndSelf(body).OfType<ReturnStatement>().Where(statement => statement.IsRef && statement.Value is not null).ToList();

        return selves.SelectMany(self => returned.SelectMany(statement =>
            Places(statement.Value!, new Frame(local => Referent(local, body), self, call, method, frame), inside)));
    }

    private static Expression? Argument(Frame frame, Parameter parameter)
    {
        var index = frame.Method!.ParametersList.IndexOf(parameter);
        return index >= 0 && index < frame.Call!.Arguments.Count && !Assigned(frame.Method.Body!, parameter) ? frame.Call.Arguments[index] : null;
    }

    private static bool Assigned(Block body, Parameter parameter)
    {
        bool names(Expression target)
        {
            return target is LocalReferenceExpression { Local.Parameter: var named } && ReferenceEquals(named, parameter);
        }

        var nodes = BodyWalker.DescendantsAndSelf(body).ToList();
        return nodes.OfType<AssignmentExpression>().SelectMany(Deconstructions.Pairs).Any(pair => names(pair.Target))
            || nodes.OfType<CompoundAssignmentExpression>().Any(compound => names(compound.Target));
    }

    private static Expression? Referent(LocalVariable local, object? root)
    {
        return root is null ? null : BodyWalker.DescendantsAndSelf(root).OfType<LocalDeclarationStatement>()
            .FirstOrDefault(declaration => ReferenceEquals(declaration.Local, local))?.Initializer;
    }

    private sealed record Frame(Func<LocalVariable, Expression?> Referent, Aliased? Self, InvocationExpression? Call, Method? Method, Frame? Caller);

    internal sealed record Aliased(Expression? Root, ImmutableArray<Field> Path)
    {
        public Aliased Child(Field field)
        {
            return this with { Path = Path.Add(field) };
        }

        public bool UnderThis => Root is null or ThisExpression;

        public string? Spelled()
        {
            var under = UnderThis ? null : OwnershipFlow.Spelled(Root!);
            var path = string.Join('.', Path.Select(field => field.Name));
            return Path.IsEmpty ? under : under is null ? path : $"{under}.{path}";
        }
    }
}