using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;

namespace Mist.ProgramAnalysis.Rules;

internal sealed partial class OwnershipFlow
{
    private void JudgeNonOwningFieldStore(Expression value, Field field)
    {
        foreach(var source in OwnedStoreSources(value).Distinct(StringComparer.Ordinal))
        {
            context.Report(ProgramAnalysisDiagnostics.OwnedValueInNonOwningField, value.Node, [value], new NameValue(source), new NameValue(field.Name));
        }
    }

    private IEnumerable<string> OwnedStoreSources(Expression value)
    {
        foreach(var selected in OwnershipAnalyzer.Selected(value))
        {
            if(ThisTiers.IsValue(selected.Type) || Origins.IsUnchangeable(selected.Type))
            {
                continue;
            }

            foreach(var source in LocalOwnership.Sources(selected).Where(Tracked))
            {
                if(LeaseOf(source.Root) is null && (source.Root.Ownership == OwnershipKind.Owned || source.Root.InfersOwnership))
                {
                    yield return source.Display;
                }
            }

            if(OwnedPathOfThis(selected) is { } held)
            {
                yield return held;
            }
        }
    }

    // The receiver's own fields have no local root for a Place to name, so a chain of owned fields from
    // 'this', written or implied, is read here. A link that is not owned ends the chain: what lies beyond
    // it is storage other references already reach.
    private static string? OwnedPathOfThis(Expression expression)
    {
        return OwnershipAnalyzer.Unwrapped(expression) switch
        {
            FieldReferenceExpression { Field: var field } when ThisTiers.IsOwned(field) => field.Name,
            MemberAccessExpression { Receiver: var receiver, Member: FieldResult { Field: var field } } when ThisTiers.IsOwned(field)
                => OwnershipAnalyzer.Unwrapped(receiver) is ThisExpression ? field.Name : OwnedPathOfThis(receiver) is { } holder ? $"{holder}.{field.Name}" : null,
            _ => null,
        };
    }
}