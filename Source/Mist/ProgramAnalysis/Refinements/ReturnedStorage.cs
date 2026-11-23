using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Refinements;

/// <summary>
/// What a reference a call returns may name besides what the call is handed by reference, which
/// <see cref="BodyHolders.Places"/> names. The callee's returned storage is read as the caller's: its <c>this</c>, and
/// an extension receiver it never reassigns, is the call's receiver, a parameter it never reassigns is the argument, and
/// a struct field lies inside its holder, so a returned ref names that storage through every nested call. A callee
/// whose body cannot be read, because it has none or another type may override it, may return any field or static of
/// the reference's type.
/// </summary>
internal static class ReturnedStorage
{
    public static WriteReach Of(Expression call, BodyHolders caller)
    {
        return Of(call, new HashSet<Method>(ReferenceEqualityComparer.Instance), caller, null);
    }

    private static WriteReach Of(Expression call, HashSet<Method> reading, BodyHolders caller, Frame? outer)
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

        var frame = new Frame(invocation, method, outer);
        var reach = WriteReach.Nothing;
        foreach(var returned in BodyWalker.DescendantsAndSelf(method.Body!).OfType<ReturnStatement>().Where(statement => statement.IsRef))
        {
            reach = reach.Or(returned.Value is { } value ? Named(value, frame, reading, caller) : WriteReach.Storage(call.Type, anyField: true));
        }

        _ = reading.Remove(method);
        return reach;
    }

    private static WriteReach Named(Expression place, Frame frame, HashSet<Method> reading, BodyHolders caller)
    {
        return place switch
        {
            FieldReferenceExpression { Field: { Modifiers.IsStatic: false } field } => Member(field, null, false, frame, caller),
            FieldReferenceExpression field => WriteReach.Field(field.Field),
            MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: var receiver } => Member(field, receiver, false, frame, caller),
            LocalReferenceExpression { Local.Parameter: { Modifier.IsRef: true } parameter } => Argument(frame, parameter) is { } argument
                ? frame.Outer is null ? caller.Reach(argument) : Named(argument, frame.Outer, reading, caller)
                : WriteReach.Storage(place.Type, anyField: true),
            IndexExpression { Indexer: null } element => WriteReach.Storage(element.Type),
            InvocationExpression nested when BodyHolders.ReturnsRef(nested) => Of(nested, reading, caller, frame),
            _ => WriteReach.Storage(place.Type, anyField: true),
        };
    }

    private static WriteReach Member(Field field, Expression? receiver, bool nested, Frame? frame, BodyHolders caller)
    {
        while(Hop(receiver) is { Modifiers: { IsRef: false, IsStatic: false } } hop && Requirement.Unrefined(hop.Type) is StructType)
        {
            (receiver, nested) = (receiver is MemberAccessExpression access ? access.Receiver : null, true);
        }

        if(frame is null)
        {
            return receiver is null ? WriteReach.Field(field) : caller.Member(field, receiver, nested);
        }

        var block = frame.Method.DeclaringBlock ?? frame.Method.ClosedBlock;
        var mapped = receiver switch
        {
            null or ThisExpression when block is null => Receiver(frame.Call),
            LocalReferenceExpression { Local: var local } when ReferenceEquals(local, block?.Receiver) && Held(frame.Method, local) => Receiver(frame.Call),
            LocalReferenceExpression { Local.Parameter: { } parameter } when Held(frame.Method, parameter) => (true, Argument(frame, parameter)),
            _ => (false, null),
        };
        return mapped is (true, var root) && (root is not null || (block is null && receiver is null or ThisExpression))
            ? Member(field, root, nested, frame.Outer, caller) : WriteReach.Field(field);
    }

    private static Field? Hop(Expression? receiver)
    {
        return receiver switch
        {
            MemberAccessExpression { Member: FieldResult { Field: var field } } => field,
            FieldReferenceExpression { Field: var field } => field,
            _ => null,
        };
    }

    private static (bool Known, Expression? Root) Receiver(InvocationExpression call)
    {
        return call.Receiver switch
        {
            MemberAccessExpression access => (true, access.Receiver),
            FunctionReferenceExpression function => (true, function.Receiver),
            MethodGroupExpression group => (true, group.Receiver),
            MethodReferenceExpression => (true, null),
            _ => (false, null),
        };
    }

    private static bool Held(Method method, Parameter parameter)
    {
        var structure = Requirement.Unrefined(parameter.Type) is StructType;
        if(structure || parameter.Modifier.IsRef)
        {
            return structure && parameter.Modifier.IsRef;
        }

        return Unassigned(method, expression => expression is LocalReferenceExpression { Local.Parameter: var named } && ReferenceEquals(named, parameter));
    }

    private static bool Held(Method method, LocalVariable receiver)
    {
        return !method.Modifiers.IsStatic && Requirement.Unrefined(receiver.Type) is not StructType
            && Unassigned(method, expression => expression is LocalReferenceExpression { Local: var named } && ReferenceEquals(named, receiver));
    }

    private static bool Unassigned(Method method, Func<Expression?, bool> names)
    {
        var nodes = BodyWalker.DescendantsAndSelf(method.Body!).ToList();
        return !(nodes.OfType<AssignmentExpression>().SelectMany(Deconstructions.Pairs).Any(pair => names(pair.Target))
            || nodes.OfType<CompoundAssignmentExpression>().Any(compound => names(compound.Target))
            || nodes.OfType<Expression>().SelectMany(BodyHolders.Handed).Any(names)
            || nodes.OfType<LocalDeclarationStatement>().Any(declaration => declaration.Local.IsRef && names(declaration.Initializer)));
    }

    private static Expression? Argument(Frame frame, Parameter parameter)
    {
        var index = frame.Method.ParametersList.IndexOf(parameter);
        return index >= 0 && index < frame.Call.Arguments.Count ? frame.Call.Arguments[index] : null;
    }

    private sealed record Frame(InvocationExpression Call, Method Method, Frame? Outer);
}