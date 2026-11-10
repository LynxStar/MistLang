using Mist.ProgramAnalysis.Flow;
using Mist.ProgramAnalysis.Rules;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;

namespace Mist.ProgramAnalysis;

// Inference is an annotation of the finished body, shared by rights, flow and cleanup consumers.
internal sealed class LocalOwnership
{
    public static readonly LocalOwnership Empty = new();

    private readonly Dictionary<LocalVariable, ThisTier> inferred = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, LocalDeclarationStatement> declarations;
    private readonly Dictionary<LocalVariable, Expression> bindings = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<LocalVariable> resolving = new(ReferenceEqualityComparer.Instance);

    private LocalOwnership()
    {
        declarations = [];
    }

    private LocalOwnership(object root)
    {
        var nodes = BodyWalker.DescendantsAndSelf(root).ToList();
        declarations = nodes.OfType<LocalDeclarationStatement>().ToDictionary(declaration => declaration.Local);
        foreach(var scope in nodes.OfType<WithBlockStatement>().Where(scope => scope.Binding is not null))
        {
            bindings[scope.Binding!] = scope.Value;
        }

        var candidates = declarations.Values.Where(declaration => declaration.Local.InfersOwnership).ToList();
        var demands = candidates.ToDictionary(declaration => declaration.Local, _ => ThisTier.None);
        var tiers = new ThisTiers();

        void demand(Expression expression, ThisTier required)
        {
            foreach(var local in OwnershipAnalyzer.Selected(expression).OfType<LocalReferenceExpression>().Select(reference => reference.Local))
            {
                if(demands.TryGetValue(local, out var known) && required > known)
                {
                    demands[local] = required;
                }
            }
        }

        void arguments(IReadOnlyList<Expression> values, IReadOnlyList<Parameter>? parameters)
        {
            for(var i = 0; parameters is not null && i < values.Count && i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                var required = parameter.Modifier.Ownership == OwnershipKind.Owned
                    || (!parameter.Modifier.IsRef && parameter.Modifier.Ownership == OwnershipKind.Unrestricted && Exclusive(values[i]))
                    ? ThisTier.Owned : ThisTiers.LendTier(parameter);
                demand(values[i], required);
            }
        }

        foreach(var node in nodes)
        {
            switch(node)
            {
                case InvocationExpression call:
                    if(ThisTiers.ReceiverOf(call) is { } receiver && call.Method is { } method)
                    {
                        demand(receiver, method.Name == "Drop" && Exclusive(receiver) ? ThisTier.Owned : tiers.Required(method));
                    }

                    arguments(call.Arguments, call.Parameters);
                    break;
                case NewExpression creation:
                    arguments(creation.Arguments, creation.Constructor?.ParametersList);
                    break;
                case ReturnStatement { Value: { } value, IsRef: false }:
                    demand(value, ThisTier.Owned);
                    break;
                case LocalDeclarationStatement { Initializer: { } value } declaration:
                    demand(value, declaration.Local.Ownership switch
                    {
                        OwnershipKind.Owned => ThisTier.Owned,
                        OwnershipKind.Borrowed => ThisTier.Borrowed,
                        OwnershipKind.Frozen => ThisTier.Frozen,
                        _ => ThisTier.None,
                    });
                    break;
                case AssignmentExpression assignment when ThisTiers.OwnedSlot(assignment.Target) is not null:
                    demand(assignment.Value, ThisTier.Owned);
                    break;
                case CompoundAssignmentExpression compound when ThisTiers.OwnedSlot(compound.Target) is not null:
                    demand(compound, ThisTier.Owned);
                    break;
            }
        }

        var uses = InferenceUses.Of(root, candidates, FlowGraphs.Shared.GraphOf(root));
        var installed = nodes.OfType<WithBlockStatement>().ToList();
        var assigned = nodes.OfType<AssignmentExpression>().Select(assignment => (assignment.Target, assignment.Value))
            .Concat(nodes.OfType<CompoundAssignmentExpression>().Select(compound => (compound.Target, Value: (Expression)compound)))
            .Where(write => write.Target is LocalReferenceExpression)
            .GroupBy(write => ((LocalReferenceExpression)write.Target).Local)
            .ToDictionary(group => group.Key, group => group.Select(write => write.Value).ToList());
        foreach(var declaration in candidates)
        {
            inferred[declaration.Local] = ThisTier.Owned;
        }

        bool changed;
        do
        {
            changed = false;
            foreach(var declaration in candidates)
            {
                if(declaration.Initializer is not { } value)
                {
                    continue;
                }

                var required = demands[declaration.Local];
                var forwarded = Sources(value).Concat(assigned.TryGetValue(declaration.Local, out var writes)
                    ? writes.SelectMany(Sources) : []);
                foreach(var source in forwarded)
                {
                    if((required > ThisTier.None || !source.IsRoot) && demands.TryGetValue(source.Root, out var known))
                    {
                        var needed = required == ThisTier.Owned ? ThisTier.Owned : ThisTier.Borrowed;
                        if(known < needed)
                        {
                            demands[source.Root] = needed;
                            changed = true;
                        }
                    }
                }
            }
        } while(changed);

        do
        {
            changed = false;
            foreach(var declaration in candidates)
            {
                var local = declaration.Local;
                if(declaration.Initializer is not { } value)
                {
                    continue;
                }

                var required = demands[local];
                var source = SourceOf(value);
                if(assigned.TryGetValue(local, out var writes))
                {
                    source = writes.Aggregate(source, (available, written) => (ThisTier)Math.Min((int)available, (int)SourceOf(written)));
                }

                var places = Sources(value).ToList();
                var pinned = places.Any(place => place.Root.IsContextBound) || installed.Any(scope => BodyWalker.DescendantsAndSelf(scope.Body).Contains(declaration)
                    && Sources(scope.Value).Any(place => places.Contains(place)));
                var later = uses.Later.Contains(local) || pinned;
                var inferredTier = places.Any(place => !place.IsRoot) ? source
                    : Choose(source, required, places.Count != 0, later, uses.Overlapping.Contains(local), Exclusive(value), uses.MutatingOverlap.Contains(local));
                if(inferredTier != inferred[local])
                {
                    inferred[local] = inferredTier;
                    changed = true;
                }


            }
        } while(changed);
    }

    public static LocalOwnership For(object root)
    {
        return FlowGraphs.Shared.OwnershipOf(root, static value => new LocalOwnership(value));
    }

    public static ThisTier Choose(ThisTier source, ThisTier required, bool forwards, bool later, bool overlapping, bool exclusive, bool mutatingOverlap = false)
    {
        if(source == ThisTier.None)
        {
            return ThisTier.None;
        }

        if(required == ThisTier.Owned)
        {
            return source;
        }

        if(!exclusive && required == ThisTier.None)
        {
            return ThisTier.None;
        }

        var cap = ThisTier.Owned;
        if(forwards && later)
        {
            cap = mutatingOverlap ? ThisTier.ReadonlyRef : overlapping ? ThisTier.Frozen : ThisTier.Borrowed;
        }

        return source < cap ? source : cap;
    }

    public ThisTier Of(LocalVariable local)
    {
        if(inferred.TryGetValue(local, out var found))
        {
            return found;
        }

        if(bindings.TryGetValue(local, out var installed) && resolving.Add(local))
        {
            var provided = SourceOf(installed);
            _ = resolving.Remove(local);
            return provided;
        }

        if(local is { Parameter: null, Ownership: OwnershipKind.Unrestricted, IsRef: false })
        {
            if(OwnershipAnalyzer.Substructural(local.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine)
            {
                return ThisTier.Owned;
            }
        }

        return ThisTiers.Declared(local);
    }

    public ThisTier Provided(Expression expression)
    {
        var leaves = OwnershipAnalyzer.Selected(expression).ToList();
        return leaves.Count == 0 ? ThisTier.None : leaves.Min(leaf => leaf switch
        {
            LocalReferenceExpression reference => Of(reference.Local),
            FieldReferenceExpression field => ThisTiers.IsOwned(field.Field) ? ThisTier.Owned : ThisTier.None,
            _ => ThisTier.Owned,
        });
    }

    private ThisTier SourceOf(Expression expression)
    {
        var leaves = OwnershipAnalyzer.Selected(expression).ToList();
        return leaves.Count == 0 ? ThisTier.None : leaves.Min(leaf => leaf switch
        {
            MemberAccessExpression { Member: FieldResult { Field: var field }, Receiver: var receiver } => ThisTiers.IsOwned(field) ? Provided(receiver) : ThisTier.None,
            InvocationExpression call when call.Signature is not ({ Modifiers.IsOwned: true } or { ReturnOwnership: OwnershipKind.Owned })
                && !Exclusive(call) && !Origins.IsUnchangeable(call.Type) => ThisTier.None,
            _ => Provided(leaf),
        });
    }

    public bool IsLoan(LocalVariable local)
    {
        return inferred.TryGetValue(local, out var tier) && tier < ThisTier.Owned
            && (tier > ThisTier.None || OwnershipAnalyzer.Substructural(local.Type) != SubstructuralDiscipline.Relevant)
            && declarations[local].Initializer is { } value && Sources(value).Any();
    }

    public bool IsView(Expression expression)
    {
        var leaves = OwnershipAnalyzer.Selected(expression).ToList();
        return leaves.Count != 0 && leaves.All(leaf => LifetimeOwners.IsView(leaf)
            || (leaf is LocalReferenceExpression reference && IsLoan(reference.Local)));
    }

    public static IEnumerable<Place> Sources(Expression expression)
    {
        return OwnershipAnalyzer.Selected(expression).Select(Place.Of).OfType<Place>().Distinct();
    }

    private static bool Exclusive(Expression expression)
    {
        return OwnershipAnalyzer.Substructural(expression.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine;
    }
}