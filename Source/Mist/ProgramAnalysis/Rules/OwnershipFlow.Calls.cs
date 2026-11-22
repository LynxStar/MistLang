using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class OwnershipFlow
{
    // A call keeps every lease and move its operands take until it returns, so each operand meets what the
    // earlier ones in the same call left. What the binding owed before the call is judged by the other checks.
    private static State CallScope(State state)
    {
        return state with { Moved = Nothing.Moved, Leases = Nothing.Leases };
    }

    private State Hold(State call, Place place, Expression at, Action action)
    {
        if(action == Action.Move)
        {
            return call with { Moved = call.Moved.SetItem(place, new Moved(at, Dropped: false)) };
        }

        Lease? lease = action switch
        {
            Action.Borrow => Lease.Borrowed,
            Action.Freeze => Lease.Frozen,
            Action.View => Lease.ReadonlyRef,
            Action.Write => Lease.Ref,
            _ => null,
        };

        if(lease is not { } kind)
        {
            return call;
        }

        var loans = ValuesOf(call.Values, place).Select(value => new Loan(place, kind, value));
        var held = call.Leases.TryGetValue(place.Root, out var prior) ? prior : [];
        return call with { Leases = call.Leases.SetItem(place.Root, held.Union(loans)) };
    }

    private bool RefusedInCall(State call, Place place, Expression at, Action action)
    {
        if(place.AncestorsAndSelf().FirstOrDefault(call.Moved.ContainsKey) is { } moved)
        {
            context.Report(ProgramAnalysisDiagnostics.UseAfterMove, at.Node, [at], new NameValue(moved.Display), new EnumValue("moved"));
            return true;
        }

        if(place.AncestorsAndSelf().FirstOrDefault(candidate => Has(call, candidate, Lease.Borrowed)) is { } borrowed)
        {
            context.Report(ProgramAnalysisDiagnostics.LeaseViolation, at.Node, [at], new NameValue(borrowed.Display), new EnumValue("access"), new EnumValue("borrowed"));
            return true;
        }

        if(Lent(call, place, action, evaluated: true) is var (lent, kind))
        {
            context.Report(ProgramAnalysisDiagnostics.LeaseViolation, at.Node, [at], new NameValue(lent.Display), new EnumValue(Verb(action)), new EnumValue(Display(kind)));
            return true;
        }

        if(Blocking(call, place, action) is var (child, state))
        {
            context.Report(ProgramAnalysisDiagnostics.CompositeState, at.Node, [at], new EnumValue(Verb(action)), new NameValue(place.Display), new NameValue(child.RelativeTo(place)), new EnumValue(Describe(state)));
            return true;
        }

        return false;
    }

    private static Action? CallAction(Expression argument, Parameter parameter)
    {
        return parameter.Modifier switch
        {
            { Direction: ByRefDirection.Out } => null,
            { IsRef: true, IsReadonly: true } => Action.View,
            { IsRef: true } => Action.Write,
            { Ownership: OwnershipKind.Borrowed } => Action.Borrow,
            { Ownership: OwnershipKind.Frozen } => Action.Freeze,
            _ when ConsumesResultArgument(argument, parameter) => Action.Move,
            _ => Action.Read,
        };
    }

    private Action ReceiverAction(InvocationExpression invocation)
    {
        if(invocation.Method is not { } method)
        {
            return Action.Read;
        }

        if(method.DeclaredThisTier == ThisTier.Owned || IsDrop(invocation))
        {
            return Action.Move;
        }

        return tiers.Required(method) switch
        {
            ThisTier.Borrowed => Action.Borrow,
            ThisTier.Frozen => Action.Freeze,
            _ => Action.View,
        };
    }
}