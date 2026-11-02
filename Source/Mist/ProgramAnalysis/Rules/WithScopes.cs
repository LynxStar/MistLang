using Mist.ProgramAnalysis.Flow;
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

        foreach(var scope in BodyWalker.DescendantsAndSelf(root).OfType<WithBlockStatement>())
        {
            if(OwnershipAnalyzer.Resolved(scope.Value) is LocalReferenceExpression { Local: var held } && Protects(held))
            {
                scopes.enters[scope.Value] = scope;
                scopes.leaves[scope.Body] = scope;
            }
            else if(scope.Binding is { } binding && Installs(binding) && !LifetimeOwners.IsView(scope.Value))
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