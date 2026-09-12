using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// A <see cref="MethodGroupExpression"/> constrained to the one overload a function-typed position
/// asks for.
/// <para>
/// An argument position reaches this TWICE, and the two halves are what make a group in an argument
/// work at all. <see cref="Accepts"/> answers applicability while
/// <see cref="OverloadResolution.Select"/> is still choosing, and <see cref="Settle"/> performs the
/// conversion once the choice is made. Neither half can stand alone: without the first the call has
/// no reason to prefer the overload whose parameter the group fits, and without the second the group
/// would reach emission, which it has no representation in.
/// </para>
/// <para>
/// A group left with NO parameter to settle against is silent, because the selection that failed to
/// supply one has already reported itself; refusing it here too would say the same thing twice. One
/// whose parameter refuses it is refused here, since nothing further would say so at all.
/// </para>
/// </summary>
internal static class MethodGroupConversion
{
    /// <summary>
    /// A group in a slot expecting a function type resolves to the one overload whose signature IS that
    /// type, selected by the TARGET rather than by arguments since there are none.
    /// <para>
    /// Identity per position rather than assignability, matching how the CLR binds a delegate: a method
    /// taking an <c>int</c> does not convert to a <c>func&lt;long, ...&gt;</c> even though the argument
    /// would widen at a call. A GENERIC method is declined, its type arguments being inferable only from
    /// the target, which is inference this does not do.
    /// </para>
    /// </summary>
    internal static Expression? Convert(
        MethodGroupExpression group, IMistType? targetType, TypeInterner interner)
    {
        if(targetType is not IFunctionType target)
        {
            return null;
        }

        Method? match = null;

        foreach(var candidate in group.Candidates)
        {
            var substitution = TypeSubstitution.ForPath(
                group.ReceiverConstructed, group.Paths?.GetValueOrDefault(candidate), interner);

            if(!SignatureIs(candidate, target, substitution, interner))
            {
                continue;
            }

            if(match is not null)
            {
                return null;
            }

            match = candidate;
        }

        return match is null ? null : new FunctionReferenceExpression(group.Node, group.Receiver, match, target);
    }

    internal static bool Accepts(MethodGroupExpression group, IMistType parameterType, TypeInterner interner)
    {
        return Convert(group, parameterType, interner) is not null;
    }

    internal static IReadOnlyList<Expression> Settle(
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<Parameter>? parameters,
        TypeSubstitution substitution,
        BuilderContext context)
    {
        if(!arguments.Any(argument => argument is MethodGroupExpression))
        {
            return arguments;
        }

        var interner = context.ProgramModel.TypeInterner;
        var settled = new List<Expression>(arguments.Count);

        for(var i = 0; i < arguments.Count; i++)
        {
            if(arguments[i] is not MethodGroupExpression group)
            {
                settled.Add(arguments[i]);
                continue;
            }

            var target = parameters is not null && i < parameters.Count
                ? substitution.Apply(parameters[i].Type, interner)
                : null;

            settled.Add(Convert(group, target, interner) ?? Refuse(group, target, context));
        }

        return settled;
    }

    private static Expression Refuse(MethodGroupExpression group, IMistType? target, BuilderContext context)
    {
        if(target is not null)
        {
            context.Diagnostics.ReportAtNode(
                ProgramModelDiagnostics.MethodGroupNotConverted, group.Node, null, group.MemberName);
        }

        return new ErrorExpression(group.Node);
    }

    private static bool SignatureIs(
        Method candidate, IFunctionType target, TypeSubstitution substitution, TypeInterner interner)
    {
        if(candidate.ParametersList.Count != target.ParameterTypes.Count
            || candidate.TypeParametersList.Count > 0)
        {
            return false;
        }

        for(var i = 0; i < candidate.ParametersList.Count; i++)
        {
            var parameter = substitution.Apply(candidate.ParametersList[i].Type, interner);

            if(!ConversionRelation.IsIdentity(parameter, target.ParameterTypes[i]))
            {
                return false;
            }
        }

        return ConversionRelation.IsIdentity(
            substitution.Apply(candidate.ReturnType, interner), target.ReturnType);
    }
}