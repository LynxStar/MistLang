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
        return OwnedStoreSources(value, []);
    }

    private IEnumerable<string> OwnedStoreSources(Expression value, HashSet<LocalVariable> seen)
    {
        foreach(var selected in OwnershipAnalyzer.Selected(value))
        {
            if(ThisTiers.IsValue(selected.Type) || Origins.IsUnchangeable(selected.Type))
            {
                continue;
            }

            if(OwnershipAnalyzer.Unwrapped(selected) is LocalReferenceExpression { Local: var alias } && Aliased(alias) is { } aliased)
            {
                foreach(var source in seen.Add(alias) ? OwnedStoreSources(aliased, seen) : [])
                {
                    yield return source;
                }

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

    private Field? RefusedAliasStore(LocalReferenceExpression target, Expression value)
    {
        return AliasedField(target.Local, []) is { } field && !ThisTiers.IsOwned(field) && OwnedStoreSources(value).Any() ? field : null;
    }

    private Field? AliasedField(LocalVariable alias, HashSet<LocalVariable> seen)
    {
        return !seen.Add(alias) ? null : Aliased(alias) switch
        {
            FieldReferenceExpression reference => reference.Field,
            MemberAccessExpression { Member: FieldResult found } => found.Field,
            LocalReferenceExpression { Local: var through } => AliasedField(through, seen),
            _ => null,
        };
    }

    // A ref local stands for the storage its declaration names, so a store through it or of it is judged
    // as a store to or of that storage. A ref parameter has no declaration here: what it stands for is
    // the caller's and stays unknown.
    private Expression? Aliased(LocalVariable alias)
    {
        return alias.IsRef && declaringBlocks.TryGetValue(alias, out var home)
            && home.Statements.OfType<LocalDeclarationStatement>().FirstOrDefault(candidate => ReferenceEquals(candidate.Local, alias)) is { Initializer: { } aliased }
            ? OwnershipAnalyzer.Unwrapped(aliased) : null;
    }

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