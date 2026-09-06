using Mist.Generated;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

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
        BuilderContext context)
    {
        if(argumentList is null)
        {
            return [];
        }

        var expressions = argumentList.GetAllExpressions();
        var bound = new List<Expression>(expressions.Length);

        for(var i = 0; i < expressions.Length; i++)
        {
            var target = targets is not null && i < targets.Count ? targets[i] : null;

            // Pushed even when null, so that an enclosing target type stops here rather than
            // reaching an argument it has nothing to say about.
            using(ctx.PushTargetType(target))
            {
                bound.Add(ExpressionBinder.BindExpression(expressions[i], ctx, context));
            }
        }

        return bound;
    }

    /// <summary>
    /// Binds each argument with the corresponding parameter type as its target, where
    /// <paramref name="candidates"/> holds exactly one member of the right arity. The target is
    /// substituted against <paramref name="receiverConstructed"/>, so an argument to
    /// <c>new Result&lt;void&gt;(...)</c> sees Void, not T -- target-typed forms such as the void
    /// literal read the slot's CLOSED type.
    /// </summary>
    internal static IReadOnlyList<Expression> Bind<T>(
        ArgumentListNode? argumentList,
        IReadOnlyList<T> candidates,
        BindingContext ctx,
        BuilderContext context,
        ConstructedType? receiverConstructed = null)
        where T : Method
    {
        var count = argumentList?.GetAllExpressions().Length ?? 0;
        var substitution = TypeSubstitution.ForReceiver(receiverConstructed);
        var interner = context.ProgramModel.TypeInterner;

        var targets = SoleByArity(candidates, count)?
            .Select(parameter => substitution.Apply(parameter.Type, interner))
            .ToList();

        return BindTargeted(argumentList, targets, ctx, context);
    }

    /// <summary>
    /// The parameters of the one candidate taking this many arguments, or null where none or several
    /// do. Several cannot be narrowed here, and that is not a gap to close later: choosing between
    /// them needs the argument types, which are what this is about to produce.
    /// </summary>
    private static IReadOnlyList<Parameter>? SoleByArity<T>(IReadOnlyList<T> candidates, int argumentCount)
        where T : Method
    {
        IReadOnlyList<Parameter>? sole = null;

        foreach(var candidate in candidates)
        {
            if(candidate.ParametersList.Count != argumentCount)
            {
                continue;
            }

            if(sole is not null)
            {
                return null;
            }

            sole = candidate.ParametersList;
        }

        return sole;
    }
}