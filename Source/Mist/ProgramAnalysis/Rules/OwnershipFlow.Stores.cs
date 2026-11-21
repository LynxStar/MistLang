using Mist.Common.Diagnostics;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

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
            else if(ownedReceiver && selected is ThisExpression)
            {
                yield return "this";
            }
        }
    }

    // A write through a ref local is a write to the storage it stands for. When that storage is a
    // non-owning field and the value is owned, the store is refused like one that names the field, and
    // nothing moves. A ref local over any other storage, an owned field included, stays a local write.
    private Field? RefusedAliasStore(LocalReferenceExpression target, Expression value)
    {
        return AliasedField(target.Local, []) is { } field && !ThisTiers.IsOwned(field) && OwnedStoreSources(value).Any() ? field : null;
    }

    // Read from the declaration that made the alias, through any ref local it was itself taken from. A
    // ref parameter has no declaration here, so what it stands for is the caller's and stays unknown.
    private Field? AliasedField(LocalVariable alias, HashSet<LocalVariable> seen)
    {
        if(!alias.IsRef || !seen.Add(alias) || !declaringBlocks.TryGetValue(alias, out var home))
        {
            return null;
        }

        var declaration = home.Statements.OfType<LocalDeclarationStatement>().First(candidate => ReferenceEquals(candidate.Local, alias));

        return declaration.Initializer is not { } aliased ? null : OwnershipAnalyzer.Unwrapped(aliased) switch
        {
            FieldReferenceExpression reference => reference.Field,
            MemberAccessExpression { Member: FieldResult found } => found.Field,
            LocalReferenceExpression { Local: var through } => AliasedField(through, seen),
            _ => null,
        };
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