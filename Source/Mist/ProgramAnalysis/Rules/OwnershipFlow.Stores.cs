using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Binding;
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

    private IEnumerable<string> OwnedStoreSources(Expression value, HashSet<LocalVariable> seen, bool holder = false)
    {
        foreach(var selected in OwnershipAnalyzer.Selected(value))
        {
            if(!holder && (ThisTiers.IsValue(selected.Type) || Origins.IsUnchangeable(selected.Type)))
            {
                continue;
            }

            if(OwnershipAnalyzer.Unwrapped(selected) is LocalReferenceExpression { Local: var alias } && Aliased(alias) is { } aliased)
            {
                foreach(var source in seen.Add(alias) ? OwnedStoreSources(aliased, seen, holder) : [])
                {
                    yield return source;
                }

                continue;
            }

            var placed = false;

            foreach(var source in LocalOwnership.Sources(selected).Where(Tracked))
            {
                if(LeaseOf(source.Root) is null && (source.Root.Ownership == OwnershipKind.Owned || source.Root.InfersOwnership))
                {
                    placed = true;
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
            else if(!placed && HeldStorage(selected) is var (receiver, member))
            {
                foreach(var owner in OwnedStoreSources(receiver, seen, holder: true).Distinct(StringComparer.Ordinal))
                {
                    yield return $"{owner}.{member}";
                }
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

    private static (Expression Receiver, string Member)? HeldStorage(Expression expression)
    {
        return OwnershipAnalyzer.Unwrapped(expression) switch
        {
            MemberAccessExpression { Receiver: var receiver, Member: FieldResult { Field: var field } } when Place.Holds(field) => (receiver, field.Name),
            MemberAccessExpression { Receiver: var receiver, Member: PropertyResult { Property: var property } } when Holds(property) => (receiver, property.Name),
            _ => null,
        };
    }

    private static bool Holds(Property property)
    {
        return OwnedProvenance.IsOwned(property) && property.Getter is { IsAuto: true } && !ThisTiers.IsValue(property.Type) && !Origins.IsUnchangeable(property.Type);
    }

    private static string? OwnedPathOfThis(Expression expression)
    {
        return OwnershipAnalyzer.Unwrapped(expression) switch
        {
            FieldReferenceExpression { Field: var field } when ThisTiers.IsOwned(field) => field.Name,
            PropertyReferenceExpression { Property: var property } when Holds(property) => property.Name,
            BackingFieldReferenceExpression { Property: var backed } when OwnedProvenance.IsOwned(backed) => backed.Name,
            MemberAccessExpression { Receiver: var receiver, Member: FieldResult { Field: var field } } when ThisTiers.IsOwned(field) => PathUnderThis(receiver, field.Name),
            MemberAccessExpression { Receiver: var receiver, Member: PropertyResult { Property: var property } } when Holds(property) => PathUnderThis(receiver, property.Name),
            _ => null,
        };
    }

    private static string? PathUnderThis(Expression receiver, string member)
    {
        return OwnershipAnalyzer.Unwrapped(receiver) is ThisExpression ? member : OwnedPathOfThis(receiver) is { } holder ? $"{holder}.{member}" : null;
    }
}