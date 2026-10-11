using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Patterns;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Flow;

/// <summary>
/// The holders of a tracked value in one body, each keyed by what its references reach -- a local by
/// itself, a parameter by its <see cref="Parameter"/> since every body declares its own local for one, a
/// field of a Drop by its <see cref="Field"/> -- and each with the node whose Leave step ends it. A name
/// that only views another holder's value, a pattern binding over it or a with binding of it, is an alias
/// and resolves to that holder; a lease or a foreach element is neither and owns nothing.
/// </summary>
internal sealed class LifetimeOwners
{
    private readonly Dictionary<object, object> homes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, object> aliases = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<LocalVariable, Expression> elements = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<object, (WithBlockStatement Scope, object Owner)> scopes = new(ReferenceEqualityComparer.Instance);

    private LifetimeOwners()
    {
    }

    public Dictionary<LocalVariable, LocalDeclarationStatement> Declarations { get; } = new(ReferenceEqualityComparer.Instance);

    public List<object> Entered { get; } = [];

    public int Count => homes.Count;

    public IEnumerable<object> All => homes.Keys;

    public IEnumerable<(WithBlockStatement Scope, object Owner)> Scopes => scopes.Values;

    public static LifetimeOwners Of(object root, Method? method, ConcreteType? holder)
    {
        var owners = new LifetimeOwners();

        if(method is not null && ReferenceEquals(method.Body, root))
        {
            foreach(var parameter in method.ParametersList.Where(parameter => Owns(parameter.Type, parameter.Modifier.IsRef, parameter.Modifier.Ownership)))
            {
                owners.Enter(parameter, root);
            }

            if(method is { Name: "Drop", ParametersList.Count: 0, Modifiers.IsStatic: false } && holder is not null)
            {
                foreach(var field in holder.FieldsList.Where(field => !field.Modifiers.IsStatic && LocalLifetimes.IsTracked(field.Type)))
                {
                    owners.Enter(field, root);
                }
            }
        }

        foreach(var node in BodyWalker.DescendantsAndSelf(root))
        {
            switch(node)
            {
                case WithBlockStatement scope:
                    owners.Scope(scope);
                    break;

                case IsPatternExpression test when owners.OwnerOf(test.Scrutinee) is { } tested:
                    owners.Alias(test.Pattern, tested);
                    break;

                case SwitchExpression choice when owners.OwnerOf(choice.Scrutinee) is { } chosen:
                    foreach(var arm in choice.Arms)
                    {
                        owners.Alias(arm.Pattern, chosen);
                    }

                    break;

                case LocalFunctionStatement function:
                    foreach(var parameter in function.Function.ParametersList.Where(parameter => Owns(parameter.Type, parameter.Modifier.IsRef, parameter.Modifier.Ownership)))
                    {
                        owners.homes[parameter] = function.Body;
                    }

                    break;

                default:
                    owners.Declare(node);
                    break;
            }
        }

        return owners;
    }

    public bool Owns(object owner)
    {
        return homes.ContainsKey(owner);
    }

    public bool IsHome(object owner, object node)
    {
        return homes.TryGetValue(owner, out var home) && ReferenceEquals(home, node);
    }

    public Expression? ElementOf(LocalVariable target)
    {
        return elements.GetValueOrDefault(target);
    }

    public (WithBlockStatement Scope, object Owner)? ScopeEndingAt(object node)
    {
        return scopes.TryGetValue(node, out var scope) ? scope : null;
    }

    public object? OwnerOf(Expression expression)
    {
        return expression switch
        {
            LocalReferenceExpression { Local: var local } when homes.ContainsKey(local) => local,
            LocalReferenceExpression { Local: var local } when aliases.TryGetValue(local, out var viewed) => viewed,
            LocalReferenceExpression { Local.Parameter: { } parameter } when homes.ContainsKey(parameter) => parameter,
            _ => FieldOf(expression),
        };
    }

    public object? Introduced(LocalVariable variable)
    {
        return homes.ContainsKey(variable) ? variable
            : variable.Parameter is { } parameter && homes.ContainsKey(parameter) ? parameter
            : null;
    }

    public object? TargetOf(Expression target)
    {
        return target switch
        {
            LocalReferenceExpression { Local: var local } when homes.ContainsKey(local) => local,
            LocalReferenceExpression { Local.Parameter: { } parameter } when homes.ContainsKey(parameter) => parameter,
            _ => FieldOf(target),
        };
    }

    public static bool Owns(IMistType type, bool isRef, OwnershipKind ownership)
    {
        return LocalLifetimes.IsTracked(type) && !isRef && ownership is not (OwnershipKind.Frozen or OwnershipKind.Borrowed);
    }

    public static bool IsView(Expression expression)
    {
        return LocalLifetimes.IsTracked(expression.Type) && expression switch
        {
            LocalReferenceExpression { Local: var local } => local.IsRef || local.Ownership is OwnershipKind.Frozen or OwnershipKind.Borrowed
                || local.Parameter is { Modifier: { IsRef: true } or { Ownership: OwnershipKind.Frozen or OwnershipKind.Borrowed } },
            FieldReferenceExpression or MemberAccessExpression { Member: FieldResult } => true,
            _ => false,
        };
    }

    private object? FieldOf(Expression expression)
    {
        var field = expression switch
        {
            FieldReferenceExpression reference => reference.Field,
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult found } => found.Field,
            _ => null,
        };

        return field is not null && homes.ContainsKey(field) ? field : null;
    }

    private void Enter(object owner, object root)
    {
        homes[owner] = root;
        Entered.Add(owner);
    }

    private void Declare(object node)
    {
        switch(node)
        {
            case Block block:
                foreach(var statement in block.Statements)
                {
                    if(statement is LocalDeclarationStatement declaration)
                    {
                        Declare(declaration, block);
                    }
                    else if(statement is TupleDeconstructionStatement { Initializer: TupleLiteralExpression tuple } deconstruction
                        && tuple.Elements.Count == deconstruction.Targets.Count)
                    {
                        for(var i = 0; i < tuple.Elements.Count; i++)
                        {
                            if(Owns(deconstruction.Targets[i]))
                            {
                                homes[deconstruction.Targets[i]] = block;
                                elements[deconstruction.Targets[i]] = tuple.Elements[i];
                            }
                        }
                    }
                }

                break;

            case ForStatement { Initializer: LocalDeclarationStatement initializer } loop:
                Declare(initializer, loop);
                break;
        }
    }

    private void Declare(LocalDeclarationStatement declaration, object home)
    {
        if(!LocalLifetimes.IsTracked(declaration.Local.Type))
        {
            return;
        }

        Declarations[declaration.Local] = declaration;

        if(Owns(declaration.Local))
        {
            homes[declaration.Local] = home;
        }
    }

    private void Scope(WithBlockStatement scope)
    {
        if(OwnerOf(scope.Value) is { } held)
        {
            scopes[scope.Body] = (scope, held);

            if(scope.Binding is { } named)
            {
                aliases[named] = held;
            }
        }
        else if(scope.Binding is { } binding && !IsView(scope.Value) && Owns(binding))
        {
            homes[binding] = scope.Body;
        }
    }

    private void Alias(Pattern pattern, object owner)
    {
        if(pattern.Binding is { } binding && LocalLifetimes.IsTracked(binding.Type))
        {
            aliases[binding] = owner;
        }

        switch(pattern)
        {
            case TypeTestPattern { Refinement: { } refinement }:
                Alias(refinement, owner);
                break;

            case NegatedPattern negated:
                Alias(negated.Operand, owner);
                break;

            case ConjunctionPattern conjunction:
                foreach(var operand in conjunction.Operands)
                {
                    Alias(operand, owner);
                }

                break;

            case DisjunctionPattern disjunction:
                foreach(var operand in disjunction.Operands)
                {
                    Alias(operand, owner);
                }

                break;
        }
    }

    private static bool Owns(LocalVariable local)
    {
        return Owns(local.Type, local.IsRef, local.Ownership);
    }
}