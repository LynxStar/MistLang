using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis.Rules;

internal sealed class WithScopes
{
    private readonly Dictionary<object, WithBlockStatement> enters = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Block, WithBlockStatement> leaves = new(ReferenceEqualityComparer.Instance);

    public static WithScopes Of(object root)
    {
        var scopes = new WithScopes();

        var locals = LocalOwnership.For(root);

        foreach(var scope in BodyWalker.DescendantsAndSelf(root).OfType<WithBlockStatement>())
        {
            var leaves = OwnershipAnalyzer.Selected(scope.Value).ToList();
            if(OwnershipAnalyzer.Resolved(scope.Value) is LocalReferenceExpression reference && Protects(reference.Local))
            {
                scopes.enters[scope.Value] = scope;
                scopes.leaves[scope.Body] = scope;
            }
            else if(leaves.Any(leaf => Place.Of(leaf) is { } place && Protects(place.Root)))
            {
                foreach(var leaf in leaves)
                {
                    scopes.enters[leaf] = scope;
                }

                scopes.leaves[scope.Body] = scope;
            }
            else if(scope.Binding is { } binding && Installs(binding) && !locals.IsView(scope.Value))
            {
                scopes.enters[scope] = scope;
                scopes.leaves[scope.Body] = scope;
            }
        }

        return scopes;
    }

    public WithBlockStatement? EnteredAt(object node)
    {
        return enters.GetValueOrDefault(node);
    }

    public WithBlockStatement? Leaving(Block body)
    {
        return leaves.GetValueOrDefault(body);
    }

    private static bool Installs(LocalVariable local)
    {
        return Protects(local) && !local.IsRef && local.Ownership is not (OwnershipKind.Frozen or OwnershipKind.Borrowed)
            && local.Parameter is not { Modifier: { IsRef: true } or { Ownership: OwnershipKind.Frozen or OwnershipKind.Borrowed } };
    }

    private static bool Protects(LocalVariable local)
    {
        return OwnershipAnalyzer.Substructural(local.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine
            && PurityJudge.Drops(local.Type, awaitable: false).Any();
    }
}