using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class OwnershipFlow
{
    private static string? NonOwningKind(LocalVariable local)
    {
        if(local.IsRef || local.Parameter is { Modifier.IsRef: true })
        {
            return local.IsReadonly || local.Parameter is { Modifier.IsReadonly: true } ? "readonly ref" : "ref";
        }

        return (local.Parameter?.Modifier.Ownership ?? local.Ownership) switch
        {
            OwnershipKind.Frozen => "frozen",
            OwnershipKind.Borrowed => "borrowed",
            _ => null,
        };
    }

    private static string? LoanAccess(State state, Place place)
    {
        if(!Place.Tracked(place))
        {
            return null;
        }

        if(NonOwningKind(place.Root) is { } own)
        {
            return own;
        }

        foreach(var candidate in place.AncestorsAndSelf())
        {
            var values = ValuesOf(state.Values, candidate);

            foreach(var value in values.OfType<LocalVariable>())
            {
                if(NonOwningKind(value) is { } inherited)
                {
                    return inherited;
                }
            }

            foreach(var loan in state.Leases.Values.SelectMany(loans => loans))
            {
                if(loan.Kind == Lease.Borrowed && loan.Lender.Equals(candidate))
                {
                    continue;
                }

                var reaches = loan.Kind is Lease.ReadonlyRef or Lease.Ref
                    ? values.Overlaps(ValuesOf(state.Values, loan.Lender))
                    : values.Contains(loan.Value);

                if(reaches)
                {
                    return Display(loan.Kind);
                }
            }
        }

        return null;
    }

    private void RefuseLoanConsumption(Place place, Expression at, string action, string kind)
    {
        context.Report(ProgramAnalysisDiagnostics.LeaseViolation, at.Node, [at], new NameValue(place.Display), new EnumValue(action), new EnumValue(kind));
    }
}