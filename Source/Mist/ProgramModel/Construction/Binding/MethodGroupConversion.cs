using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Scopes;
using Mist.ProgramModel.Model.Types;

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
    /// would widen at a call. A GENERIC method takes the type arguments stated after its name, or else reads
    /// them off the target's positions (<see cref="TypeArgumentInference.FromSignature"/>), and is then held
    /// to the same identity; one the target leaves open is declined, and yields to a plain overload of the
    /// same signature as it does at a call, unless arguments were stated, which only a method of that arity
    /// can take. Two overloads the receiver's arguments make one
    /// signature are ordered as a call orders them, by <see cref="OverloadResolution.MoreSpecificSignature"/>
    /// (method-overloading Requirement 3.12); a pair no position separates is ambiguous.
    /// </para>
    /// </summary>
    internal static Expression? Convert(
        MethodGroupExpression group, IMistType? targetType, TypeInterner interner)
    {
        return Convert(group, targetType, interner, out _);
    }

    internal static Expression? Convert(
        MethodGroupExpression group, IMistType? targetType, TypeInterner interner, out bool ambiguous)
    {
        ambiguous = false;

        if(targetType is not IFunctionType target)
        {
            return null;
        }

        var preferred = Preferred(group, target.ParameterTypes, target.ReturnType, interner);

        if(preferred is not [var (match, substitution)])
        {
            ambiguous = preferred.Count > 1;
            return null;
        }

        return new FunctionReferenceExpression(group.Node, group.Receiver, match, target)
        {
            TypeArguments = match.TypeParametersList.ConvertAll(parameter => substitution.Apply(parameter, interner)),
            ContextTypes = ContextBinding.TypesOf(match, type => substitution.Apply(type, interner)),
        };
    }

    /// <summary>
    /// What the one overload taking exactly these parameter types returns, for a call that learns its own
    /// type arguments from a group's result, or null where no overload or several do. The same choice
    /// <see cref="Convert"/> makes, with the result left for the overload to say.
    /// </summary>
    internal static IMistType? ResultType(
        MethodGroupExpression group, IReadOnlyList<IMistType> parameterTypes, TypeInterner interner)
    {
        return Preferred(group, parameterTypes, returnType: null, interner) is [var (match, substitution)]
            ? substitution.Apply(match.ReturnType, interner)
            : null;
    }

    private static List<(Method Method, TypeSubstitution Substitution)> Preferred(
        MethodGroupExpression group, IReadOnlyList<IMistType> parameterTypes, IMistType? returnType, TypeInterner interner)
    {
        TypeSubstitution substitutionOf(Method candidate)
        {
            return Receiver(group, candidate, interner);
        }

        var closed = group.Candidates
            .Select(candidate => (Candidate: candidate, Substitution: Closing(candidate, group.StatedTypeArguments, parameterTypes, returnType, substitutionOf(candidate), interner)))
            .Where(candidate => candidate.Substitution is not null)
            .ToDictionary(candidate => candidate.Candidate, candidate => candidate.Substitution!);

        var matches = OverloadResolution.WithoutHidden([.. closed.Keys], substitutionOf, interner);

        return matches
            .Where(candidate => !matches.Exists(other => !ReferenceEquals(other, candidate)
                && OverloadResolution.MoreSpecificSignature(other, candidate, parameterTypes.Count)))
            .Select(candidate => (candidate, closed[candidate]))
            .ToList();
    }

    internal static Expression? Convert(
        MethodGroupExpression group, IMistType? targetType, BuilderContext context, out bool ambiguous, Scope? boundIn = null)
    {
        var converted = Convert(group, targetType, context.ProgramModel.TypeInterner, out ambiguous);

        if(converted is FunctionReferenceExpression { TypeArguments.Count: > 0 } reference)
        {
            var closing = Receiver(group, reference.Method, context.ProgramModel.TypeInterner)
                .Extend(TypeSubstitution.ForMethod(reference.Method, reference.TypeArguments));

            context.ProgramModel.ConstructorConstraintClosures.RecordMethod(reference.Method, reference.TypeArguments, closing, group.Node, boundIn);
            context.ProgramModel.VariadicUses.Record(reference.Method, reference.TypeArguments);
            context.ProgramModel.GenericCalls.Record(reference.Method, reference.TypeArguments);
        }

        return converted;
    }

    private static TypeSubstitution Receiver(MethodGroupExpression group, Method candidate, TypeInterner interner)
    {
        return TypeSubstitution.ForPath(group.ReceiverConstructed, group.Paths?.GetValueOrDefault(candidate), interner);
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

            settled.Add(Convert(group, target, context, out var ambiguous) ?? Refuse(group, target, ambiguous, context));
        }

        return settled;
    }

    private static Expression Refuse(MethodGroupExpression group, IMistType? target, bool ambiguous, BuilderContext context)
    {
        if(target is not null)
        {
            context.Diagnostics.ReportAtNodeTyped(
                ambiguous ? ProgramModelDiagnostics.MethodGroupAmbiguous : ProgramModelDiagnostics.MethodGroupNotConverted,
                group.Node, null, new NameValue(group.MemberName));
        }

        return new ErrorExpression(group.Node);
    }

    private static TypeSubstitution? Closing(
        Method candidate,
        IReadOnlyList<IMistType>? stated,
        IReadOnlyList<IMistType> parameterTypes,
        IMistType? returnType,
        TypeSubstitution substitution,
        TypeInterner interner)
    {
        if(stated is not null && stated.Count != candidate.TypeParametersList.Count)
        {
            return null;
        }

        if(candidate.TypeParametersList.Count > 0)
        {
            if((stated ?? TypeArgumentInference.FromSignature(candidate, parameterTypes, returnType)) is not { } closing)
            {
                return null;
            }

            substitution = substitution.Extend(TypeSubstitution.ForMethod(candidate, closing));
        }

        return SignatureIs(candidate, parameterTypes, returnType, substitution, interner) ? substitution : null;
    }

    private static bool SignatureIs(
        Method candidate,
        IReadOnlyList<IMistType> parameterTypes,
        IMistType? returnType,
        TypeSubstitution substitution,
        TypeInterner interner)
    {
        var parameters = ValuePack.Expand(candidate.ParametersList, substitution, interner);
        if(parameters.Count != parameterTypes.Count)
        {
            return false;
        }

        for(var i = 0; i < parameters.Count; i++)
        {
            if(!IsTheSame(parameters[i].Type, parameterTypes[i]))
            {
                return false;
            }
        }

        return returnType is null || IsTheSame(substitution.Apply(candidate.ReturnType, interner), returnType);
    }

    private static bool IsTheSame(IMistType declared, IMistType expected)
    {
        return (declared is TypeParameter && ReferenceEquals(declared, expected)) || ConversionRelation.IsIdentity(declared, expected);
    }
}