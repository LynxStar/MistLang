using Mist.Common.Diagnostics;
using Mist.Generated;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;
using Storm.Runtime;

namespace Mist.ProgramModel.Construction.Binding.Expressions;

/// <summary>
/// Binds an argument list. The single place every call-shaped construct goes through:
/// invocations and method calls, object creation, and constructor chains. Keeping them
/// on one path means argument-level concerns -- propagating a target type from the
/// selected overload's parameters, and honouring <c>ref</c> arguments, the latter of which
/// is not handled yet -- are added once rather than per call site.
/// </summary>
internal static class ArgumentBinder
{
    /// <summary>
    /// Which arguments were WRITTEN <c>ref</c>, positionally. Kept apart from the bound expressions
    /// because it is not a property of an expression -- the same expression is a different argument
    /// depending on the keyword in front of it -- and because only overload SELECTION needs it.
    /// Emission reads the keyword back off the parameter that was chosen, so nothing downstream of
    /// the choice has to carry this.
    /// <para>
    /// Empty when there is no argument list, which reads as "no argument is ref" for the zero
    /// arguments there are.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<bool> RefFlags(ArgumentListNode? argumentList)
    {
        if(argumentList is null)
        {
            return [];
        }

        var flags = new List<bool>(1 + argumentList.Arguments.Length)
        {
            argumentList.Argument is RefArgumentArgumentNode,
        };

        foreach(var argument in argumentList.Arguments)
        {
            flags.Add(argument is RefArgumentArgumentNode);
        }

        return flags;
    }

    /// <summary>
    /// Binds each argument in <paramref name="argumentList"/> in order, or returns an
    /// empty list when the construct has no argument list at all (as in <c>f()</c>). Nothing targets
    /// the slots, for a caller with no candidates to read parameters off.
    /// </summary>
    internal static IReadOnlyList<Expression> Bind(
        ArgumentListNode? argumentList, BindingContext ctx, BuilderContext context)
    {
        return BindTargeted(argumentList, targets: null, ctx, context);
    }

    /// <summary>
    /// Binds each argument against the target at its position, for a caller whose slots are TYPES rather
    /// than declared parameters: invoking a function-typed value has a signature but no
    /// <see cref="Parameter"/> objects to read one off.
    /// </summary>
    internal static IReadOnlyList<Expression> BindTargeted(
        ArgumentListNode? argumentList,
        IReadOnlyList<IMistType>? targets,
        BindingContext ctx,
        BuilderContext context,
        bool settlesMethodGroups = false,
        bool defersLambdas = false,
        PackSlot? absorbing = null)
    {
        if(argumentList is null)
        {
            return [];
        }

        var expressions = argumentList.GetAllExpressions();
        var bound = new List<Expression>(expressions.Length);

        for(var i = 0; i < expressions.Length; i++)
        {
            if(defersLambdas && expressions[i] is LambdaFormExpressionNode { Lambda: { } lambda })
            {
                bound.Add(new UnboundLambdaExpression(lambda));
                continue;
            }

            var target = targets is not null && i < targets.Count ? targets[i] : null;

            // Pushed even when null, so that an enclosing target type stops here rather than
            // reaching an argument it has nothing to say about.
            using(ctx.PushTargetType(target))
            using(ctx.PushMethodGroupSettlement(settlesMethodGroups ? ValueOf(expressions[i]) : null))
            {
                Add(
                    ExpressionBinder.BindExpression(expressions[i], ctx, context), target,
                    absorbing is { } slot && slot.Index == i ? slot : null, bound, context);
            }
        }

        return bound;
    }

    /// <summary>
    /// The argument position a value pack parameter takes, and the pack the call is still to infer for
    /// it. A spread there stays a spread, the parameter being the flat list it stands for; anywhere else
    /// a slot typed as the pack's tuple takes the tuple itself.
    /// </summary>
    internal readonly record struct PackSlot(int Index, TypeParameter? Inferred);

    private static PostfixNode? ValueOf(SyntaxNode argument)
    {
        PostfixNode? value = null;
        var node = argument;

        while(node is not null)
        {
            value = node as PostfixNode ?? value;

            var children = node.Children().Where(child => child is not null).Take(2).ToList();
            node = children.Count == 1 ? children[0] : null;
        }

        return value;
    }

    /// <summary>
    /// One argument, or the several a spread stands for. Expanded HERE rather than where the suffix
    /// was bound, because how many slots it owes is the call's question: everything downstream --
    /// arity, overload choice, inference -- counts arguments, and a spread that reached any of them
    /// unexpanded would count as one.
    /// </summary>
    private static void Add(
        Expression argument, IMistType? target, PackSlot? absorbing, List<Expression> bound, BuilderContext context)
    {
        if(argument is not SpreadExpression spread)
        {
            bound.Add(argument);
            return;
        }

        // A pack spread into a slot declared as THAT pack is proven for every instantiation, so it
        // survives unexpanded: there is nothing to expand it into yet and nothing that needs it to
        // be. Forwarding `args...` into a function whose parameter list is TArgs is the case, and
        // it is the one spread whose correctness does not depend on the arity being known.
        if(ProvenAgainst(spread.Operand.Type, target, absorbing?.Inferred))
        {
            bound.Add(target is SpreadType || absorbing is not null ? spread : spread.Operand);
            return;
        }

        if(Structural(spread.Operand.Type) is not { } elements)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.UnprovableSpread, spread.Node, null, spread.Operand.Type.Name);
            bound.Add(argument);
            return;
        }

        // Named positionally, which is how a tuple's elements are reachable and therefore what the
        // expansion emits as.
        for(var i = 0; i < elements.Count; i++)
        {
            bound.Add(new MemberAccessExpression(
                spread.Node, spread.Operand, $"Item{i + 1}", elements[i]));
        }
    }

    /// <summary>
    /// Whether the spread's operand and the slot it fills are the same pack, which is what makes an
    /// unknown length harmless: however many the pack turns out to hold, the slot holds that many.
    /// </summary>
    private static bool ProvenAgainst(IMistType operand, IMistType? target, TypeParameter? inferred)
    {
        return Pack(operand) is { } spreadPack
            && target is not null
            && Pack(target) is { } targetPack
            && (ReferenceEquals(spreadPack, targetPack) || ReferenceEquals(targetPack, inferred));
    }

    /// <summary>
    /// The pack a type stands for, whether written as the tuple a value pack has or as a bare
    /// spread, or null for anything else.
    /// </summary>
    private static TypeParameter? Pack(IMistType type)
    {
        var spread = type switch
        {
            TupleType { ElementTypes: [SpreadType only] } => only,
            SpreadType bare => bare,
            _ => null,
        };

        return spread?.Operand as TypeParameter;
    }

    /// <summary>
    /// The element types a spread stands for, or null where the operand has no fixed shape -- an
    /// unbound pack among them being the case that matters, since its length is exactly what is not
    /// yet known.
    /// </summary>
    private static IReadOnlyList<IMistType>? Structural(IMistType type)
    {
        var elements = type switch
        {
            NamedTupleType named => named.Underlying.ElementTypes,
            TupleType tuple => tuple.ElementTypes,
            _ => null,
        };

        return elements is not null && !elements.Any(e => e is SpreadType) ? elements : null;
    }

    /// <summary>
    /// Binds each argument with the corresponding parameter type as its target, where
    /// <paramref name="candidates"/> holds exactly one member of the right arity. The target is
    /// substituted against <paramref name="receiverConstructed"/> and any <paramref name="typeArguments"/>
    /// the call states, so an argument to <c>new Result&lt;void&gt;(...)</c> sees Void, not T -- target-typed
    /// forms such as the void literal, and a lambda's parameters, read the slot's CLOSED type. A
    /// homogeneous params slot targets what it collects with its ELEMENT type: every argument once the
    /// count says the call is in expanded form, and a lambda always, since a lambda is never the array.
    /// </summary>
    internal static IReadOnlyList<Expression> Bind<T>(
        ArgumentListNode? argumentList,
        IReadOnlyList<T> candidates,
        BindingContext ctx,
        BuilderContext context,
        ConstructedType? receiverConstructed = null,
        IReadOnlyDictionary<Method, IReadOnlyList<ConstructedType>>? paths = null,
        IReadOnlyList<IMistType>? typeArguments = null)
        where T : Method
    {
        var count = argumentList?.GetAllExpressions().Length ?? 0;
        var interner = context.ProgramModel.TypeInterner;
        var sole = SoleByArity(candidates, count, typeArguments);
        var substitution = TypeSubstitution.ForPath(
            receiverConstructed, sole is not null && paths is { } reached ? reached.GetValueOrDefault(sole) : null, interner);

        if(sole is not null && typeArguments is not null)
        {
            substitution = substitution.Extend(TypeSubstitution.ForMethod(sole, typeArguments));
        }

        var targets = sole is null ? null : Targets(sole.ParametersList, argumentList, substitution, interner);

        var settled = sole is null
            || (typeArguments is null && sole.TypeParametersList.Count > 0 && ValuePack.FlattenedSlot(sole.ParametersList) is null);

        return BindTargeted(
            argumentList, targets, ctx, context,
            settlesMethodGroups: settled,
            defersLambdas: settled && candidates.Count > 0 && !candidates.Any(candidate => ValuePack.FlattenedSlot(candidate.ParametersList) is not null),
            absorbing: sole is null ? null : Absorbing(sole, inferring: typeArguments is null));
    }

    private static PackSlot? Absorbing(Method sole, bool inferring)
    {
        return ValuePack.FlattenedSlot(sole.ParametersList) is { } flattened
            && ValuePack.PackParameterOf(sole.ParametersList[flattened]) is { } pack
                ? new PackSlot(flattened, inferring && sole.TypeParametersList.Contains(pack) ? pack : null)
                : null;
    }

    /// <summary>
    /// The arguments the call left unsettled, bound against the parameter the chosen overload gives each:
    /// a lambda exactly as it would have been bound had that been the only candidate, and a group
    /// converted by <see cref="MethodGroupConversion"/>. Where nothing was chosen the selection has
    /// reported why, and a lambda is left as a failure rather than bound against nothing.
    /// </summary>
    internal static IReadOnlyList<Expression> Settle(
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<Parameter>? parameters,
        TypeSubstitution substitution,
        BindingContext ctx,
        BuilderContext context)
    {
        var interner = context.ProgramModel.TypeInterner;
        var lambdasBound = arguments
            .Select((argument, i) => argument is not UnboundLambdaExpression lambda
                ? argument
                : parameters is not null && i < parameters.Count
                    ? LambdaTrials.BindAgainst(lambda, substitution.Apply(parameters[i].Type, interner), ctx, context)
                    : new ErrorExpression(lambda.Node))
            .ToList();

        return MethodGroupConversion.Settle(lambdasBound, parameters, substitution, context);
    }

    /// <summary>
    /// The parameters of the one candidate taking this many arguments, and as many type arguments as the
    /// call states, or null where none or several do. Several cannot be narrowed here: choosing between
    /// them needs the argument types, which are what this is about to produce. An argument that cannot be
    /// bound WITHOUT a target therefore survives untyped and the selection settles it -- a method group
    /// (<see cref="MethodGroupConversion"/>) or a lambda (<see cref="UnboundLambdaExpression"/>), which a
    /// sole GENERIC candidate leaves untyped too, its parameter types waiting on the type arguments.
    /// </summary>
    private static T? SoleByArity<T>(
        IReadOnlyList<T> candidates, int argumentCount, IReadOnlyList<IMistType>? typeArguments)
        where T : Method
    {
        T? sole = null;

        foreach(var candidate in candidates)
        {
            if(!Fills(candidate.ParametersList, argumentCount)
                || (typeArguments is not null && candidate.TypeParametersList.Count != typeArguments.Count))
            {
                continue;
            }

            if(sole is not null)
            {
                return null;
            }

            sole = candidate;
        }

        return sole;
    }

    private static bool Fills(IReadOnlyList<Parameter> parameters, int argumentCount)
    {
        return parameters.Count == argumentCount
            || (ValuePack.FlattenedSlot(parameters) is { } flattened
                && ValuePack.ElementOf(parameters[flattened]) is not null
                && ValuePack.AcceptsCount(parameters, argumentCount));
    }

    private static List<IMistType> Targets(
        IReadOnlyList<Parameter> parameters, ArgumentListNode? argumentList, TypeSubstitution substitution, TypeInterner interner)
    {
        if(argumentList is null)
        {
            return [];
        }

        var expressions = argumentList.GetAllExpressions();
        var targets = new List<IMistType>(expressions.Length);

        for(var i = 0; i < expressions.Length; i++)
        {
            var parameter = parameters[ValuePack.SlotOf(parameters, expressions.Length, i)];
            var target = ValuePack.ElementOf(parameter) is { } element
                && (expressions.Length != parameters.Count || expressions[i] is LambdaFormExpressionNode)
                    ? element
                    : parameter.Type;

            targets.Add(substitution.Apply(target, interner));
        }

        return targets;
    }
}