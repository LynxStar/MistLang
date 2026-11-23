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
/// a struct field lies inside its holder, so a returned ref names that storage through every nested call. Each method's
/// returns are read once in terms of its own receiver and parameters, the least fixpoint over its <c>return ref</c>
/// statements, so a recursive call contributes what the callee's returns name through that call and a cycle that never
/// reaches known storage contributes nothing. A callee whose body cannot be read, because it has none or another type
/// may override it, may return any field or static of the reference's type.
/// </summary>
internal static class ReturnedStorage
{
    public static WriteReach Of(Expression call, BodyHolders caller)
    {
        var reading = new Reading(caller);
        return reading.Of(call, null).Aggregate(WriteReach.Nothing, (reach, returned) => reach.Or(returned switch
        {
            Reached { Reach: var known } => known,
            FieldOf { Field: var field } => WriteReach.Field(field),
            StorageOf { Place: var place, AnyField: var anyField } => WriteReach.Storage(place.Type, anyField),
            _ => WriteReach.Storage(call.Type, anyField: true),
        }));
    }

    private sealed class Reading(BodyHolders caller)
    {
        private readonly Dictionary<Method, List<Returned>> summaries = new(ReferenceEqualityComparer.Instance);
        private bool iterating;
        private bool grown;

        public List<Returned> Of(Expression call, Method? within)
        {
            if(call is not InvocationExpression { Method: { } selected } invocation)
            {
                return [new StorageOf(call, true)];
            }

            var method = selected.SourceDeclaration ?? selected;
            if((method.DeclaringType is AbstractType && !method.Modifiers.IsStatic) || method.Body is null)
            {
                return [new StorageOf(call, true)];
            }

            return Distinct(Summary(method).ToList().SelectMany(returned => Through(returned, invocation, method, within)));
        }

        private List<Returned> Summary(Method method)
        {
            if(summaries.TryGetValue(method, out var known))
            {
                return known;
            }

            summaries[method] = [];
            if(iterating)
            {
                grown = true;
                return [];
            }

            iterating = true;
            do
            {
                grown = false;
                foreach(var each in summaries.Keys.ToList())
                {
                    var next = Returns(each);
                    if(next.Count != summaries[each].Count)
                    {
                        (summaries[each], grown) = (next, true);
                    }
                }
            }
            while(grown);

            iterating = false;
            return summaries[method];
        }

        private List<Returned> Returns(Method method)
        {
            return Distinct(BodyWalker.DescendantsAndSelf(method.Body!).OfType<ReturnStatement>().Where(statement => statement.IsRef)
                .SelectMany(returned => returned.Value is { } value ? Named(value, method) : [new AnyReturned()]));
        }

        private List<Returned> Through(Returned returned, InvocationExpression call, Method method, Method? within)
        {
            return returned switch
            {
                ThisField(var field, var nested) => Receiver(call) is (true, var root) ? Member(field, root, nested, within) : [new FieldOf(field)],
                ReceiverField(var field, var nested) => Receiver(call) is (true, { } root) ? Member(field, root, nested, within) : [new FieldOf(field)],
                ParameterField(var parameter, var field, var nested) => Argument(call, method, parameter) is { } argument
                    ? Member(field, argument, nested, within) : [new FieldOf(field)],
                ByRef(var parameter, var place) => Argument(call, method, parameter) is not { } argument ? [new StorageOf(place, true)]
                    : within is null ? [new Reached(caller.Reach(argument))] : Named(argument, within),
                AnyReturned => [new StorageOf(call, true)],
                _ => [returned],
            };
        }

        private List<Returned> Named(Expression place, Method within)
        {
            return place switch
            {
                FieldReferenceExpression { Field: { Modifiers.IsStatic: false } field } => Member(field, null, false, within),
                FieldReferenceExpression field => [new FieldOf(field.Field)],
                MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: var receiver } => Member(field, receiver, false, within),
                LocalReferenceExpression { Local.Parameter: { Modifier.IsRef: true } parameter } => [new ByRef(parameter, place)],
                IndexExpression { Indexer: null } element => [new StorageOf(element, false)],
                InvocationExpression nested when BodyHolders.ReturnsRef(nested) => Of(nested, within),
                _ => [new StorageOf(place, true)],
            };
        }

        private List<Returned> Member(Field field, Expression? receiver, bool nested, Method? within)
        {
            while(Hop(receiver) is { Modifiers: { IsRef: false, IsStatic: false } } hop && Requirement.Unrefined(hop.Type) is StructType)
            {
                (receiver, nested) = (receiver is MemberAccessExpression access ? access.Receiver : null, true);
            }

            if(within is null)
            {
                return [receiver is null ? new FieldOf(field) : new Reached(caller.Member(field, receiver, nested))];
            }

            var block = within.DeclaringBlock ?? within.ClosedBlock;
            return
            [
                receiver switch
                {
                    null or ThisExpression when block is null => new ThisField(field, nested),
                    LocalReferenceExpression { Local: var local } when ReferenceEquals(local, block?.Receiver) && Held(within, local) => new ReceiverField(field, nested),
                    LocalReferenceExpression { Local.Parameter: { } parameter } when Held(within, parameter) => new ParameterField(parameter, field, nested),
                    _ => new FieldOf(field),
                },
            ];
        }

        private static List<Returned> Distinct(IEnumerable<Returned> returned)
        {
            return returned.Distinct().ToList();
        }
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

    private static Expression? Argument(InvocationExpression call, Method method, Parameter parameter)
    {
        var index = method.ParametersList.IndexOf(parameter);
        return index >= 0 && index < call.Arguments.Count ? call.Arguments[index] : null;
    }

    private abstract record Returned;

    private sealed record Reached(WriteReach Reach) : Returned;

    private sealed record FieldOf(Field Field) : Returned;

    private sealed record StorageOf(Expression Place, bool AnyField) : Returned;

    private sealed record ThisField(Field Field, bool Nested) : Returned;

    private sealed record ReceiverField(Field Field, bool Nested) : Returned;

    private sealed record ParameterField(Parameter Parameter, Field Field, bool Nested) : Returned;

    private sealed record ByRef(Parameter Parameter, Expression Place) : Returned;

    private sealed record AnyReturned : Returned;
}