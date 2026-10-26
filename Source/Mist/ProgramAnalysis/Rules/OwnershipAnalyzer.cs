using Mist.Common.Diagnostics;
using Mist.ProgramAnalysis.Flow;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Enums;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Statements;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramAnalysis.Rules;

/// <summary>
/// Ownership on a finished body: moves, leases and per-field state through <see cref="OwnershipFlow"/>,
/// owned destinations through <see cref="OwnedDestinations"/>, and the owned return judged here.
/// <para>
/// An owned return hands the caller a value nothing else holds (ownership-v2 Requirements 1.5 and
/// 6.7), so a `return` in a method returning owned is judged by <see cref="Origins"/>, without flow: a
/// construction, a clone, a call returning owned, an owned binding, a local fresh at every assignment,
/// or a value of a type that cannot change; the receiver and its owned fields count once the method
/// takes `this` owned. Anything else is refused at the value.
/// </para>
/// </summary>
internal sealed class OwnershipAnalyzer : IProgramAnalyzer
{
    public string Id => "ownership-borrow-checking";

    public void Analyze(AnalysisContext context)
    {
        var tiers = new ThisTiers();

        foreach(var root in context.Bodies)
        {
            var graph = FlowGraphBuilder.Build(root.Root);
            var owning = OwningMethod(root);
            new OwnershipFlow(context, tiers, DeclaringBlocks(root.Root), Scoped(root.Root), Parents(root.Root), owning).Judge(graph, OwnershipFlow.Nothing);

            if(owning is { Body: { } body })
            {
                JudgeOwnedReturns(context, owning, body, new Origins(body, owning, ScopeKind.Readonly));
            }

            OwnedDestinations.Judge(context, root);
        }

        foreach(var type in context.Types.OfType<ConcreteType>())
        {
            OwnedDestinations.JudgeDefaults(context, type);
        }
    }

    private static Method? OwningMethod(SourceRoot root)
    {
        return root.Member is Method { Body: { } body } method && ReferenceEquals(body, root.Root) && ReturnsOwned(method) ? method : null;
    }

    private static bool ReturnsOwned(Method method)
    {
        return method.Modifiers.IsOwned || method.ReturnOwnership == OwnershipKind.Owned;
    }

    private static void JudgeOwnedReturns(AnalysisContext context, Method method, object node, Origins origins)
    {
        foreach(var child in BodyWalker.ChildrenOf(node))
        {
            if(child is LambdaExpression or LocalFunctionStatement or AsyncBlockExpression)
            {
                continue;
            }

            if(child is ReturnStatement { Value: { } value } && !YieldsOwned(value, method, origins))
            {
                context.Report(ProgramAnalysisDiagnostics.OwnedReturnNotOwned, value.Node, [value], method.Name, Unowned(value));
            }

            JudgeOwnedReturns(context, method, child, origins);
        }
    }

    internal static bool YieldsOwned(Expression value, Method? method, Origins origins)
    {
        if(Origins.IsUnchangeable(value.Type))
        {
            return true;
        }

        if(origins.Of(value) is Origin.Own)
        {
            return Unwrapped(value) is not LocalReferenceExpression { Local: { Ownership: not OwnershipKind.Owned } local } || !origins.Escapes(local);
        }

        return method?.DeclaredThisTier == ThisTier.Owned && Unwrapped(value) switch
        {
            ThisExpression => true,
            FieldReferenceExpression field => ThisTiers.IsOwned(field.Field),
            MemberAccessExpression { Receiver: ThisExpression, Member: FieldResult found } => ThisTiers.IsOwned(found.Field),
            _ => false,
        };
    }

    internal static string? HeldByAnotherOwner(Expression value, Method? method)
    {
        var (field, receiver) = Unwrapped(value) switch
        {
            FieldReferenceExpression reference when ThisTiers.IsOwned(reference.Field) => (reference.Field.Name, null),
            MemberAccessExpression { Member: FieldResult found } access when ThisTiers.IsOwned(found.Field) => (found.Field.Name, access.Receiver),
            MemberAccessExpression { Member: PropertyResult { Property: { Modifiers: { IsOwned: true, IsStatic: false } } property } } access => (property.Name, access.Receiver),
            _ => (null, null),
        };

        return field is not null && !(method?.DeclaredThisTier == ThisTier.Owned && receiver is null or ThisExpression) ? field : null;
    }

    internal static string Unowned(Expression value)
    {
        return Unwrapped(value) switch
        {
            ThisExpression => "'this' is the receiver, which the caller still holds",
            LocalReferenceExpression { Local: { Parameter: not null } parameter } => $"parameter '{parameter.Name}' is not owned, so the caller may still hold its value",
            LocalReferenceExpression { Local: var local } => $"local '{local.Name}' is neither owned nor a fresh value nothing else keeps",
            FieldReferenceExpression field => $"field '{field.Field.Name}' is storage other references reach",
            MemberAccessExpression { Member: FieldResult found } => $"field '{found.Field.Name}' is storage other references reach",
            InvocationExpression { Method: { } called } => $"'{called.Name}' does not return owned",
            _ => "it is neither fresh, moved from an owned binding, nor of a type that cannot change",
        };
    }

    internal static Expression Unwrapped(Expression expression)
    {
        return expression switch
        {
            ImplicitConversionExpression { Operator: null } conversion => Unwrapped(conversion.Operand),
            CastExpression { Operator: null } cast => Unwrapped(cast.Operand),
            AsExpression converted => Unwrapped(converted.Operand),
            _ => expression,
        };
    }

    private static Dictionary<LocalVariable, Block> DeclaringBlocks(object root)
    {
        var blocks = new Dictionary<LocalVariable, Block>(ReferenceEqualityComparer.Instance);

        foreach(var block in BodyWalker.DescendantsAndSelf(root).OfType<Block>())
        {
            foreach(var declaration in block.Statements.OfType<LocalDeclarationStatement>())
            {
                blocks[declaration.Local] = block;
            }
        }

        return blocks;
    }

    private static Dictionary<object, object> Parents(object root)
    {
        var parents = new Dictionary<object, object>(ReferenceEqualityComparer.Instance);

        foreach(var node in BodyWalker.DescendantsAndSelf(root))
        {
            foreach(var child in BodyWalker.ChildrenOf(node))
            {
                parents[child] = node;
            }
        }

        return parents;
    }

    /// <summary>
    /// The with blocks that release the linear or affine local they were given, keyed by the body whose end
    /// releases it (drop-semantics Requirements 3.2 and 5.5): from there on the local is dropped. A lease
    /// is given only for the block's duration and stays its owner's.
    /// </summary>
    private static Dictionary<Block, LocalReferenceExpression> Scoped(object root)
    {
        var scoped = new Dictionary<Block, LocalReferenceExpression>(ReferenceEqualityComparer.Instance);

        foreach(var scope in BodyWalker.DescendantsAndSelf(root).OfType<WithBlockStatement>())
        {
            if(scope.Value is LocalReferenceExpression { Local: var held } given
                && Tracked(held)
                && Substructural(held.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine
                && held is { IsRef: false, Ownership: not (OwnershipKind.Frozen or OwnershipKind.Borrowed) }
                && held.Parameter is not { Modifier: { IsRef: true } or { Ownership: OwnershipKind.Frozen or OwnershipKind.Borrowed } }
                && PurityJudge.Drops(held.Type, awaitable: false).Any())
            {
                scoped[scope.Body] = given;
            }
        }

        return scoped;
    }

    internal static bool Tracked(LocalVariable local)
    {
        return (local.Ownership == OwnershipKind.Owned && !ThisTiers.IsValue(local.Type))
            || Substructural(local.Type) is SubstructuralDiscipline.Linear or SubstructuralDiscipline.Affine;
    }

    internal static SubstructuralDiscipline Substructural(IMistType type)
    {
        return (type is ConstructedType constructed ? constructed.Definition : type) is ConcreteType concrete
            ? concrete.Discipline
            : SubstructuralDiscipline.None;
    }
}