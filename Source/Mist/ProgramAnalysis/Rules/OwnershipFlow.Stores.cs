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

            var held = selected switch
            {
                FieldReferenceExpression reference when ThisTiers.IsOwned(reference.Field) => reference.Field,
                MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult found } when ThisTiers.IsOwned(found.Field) => found.Field,
                _ => null,
            };

            if(held is not null)
            {
                yield return held.Name;
            }
        }
    }
}