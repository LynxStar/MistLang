using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// Chooses which overload a call or an index selects, in three stages: filter by arity, keep the
/// candidates every argument can reach, then take the best of those. Reporting a failed choice belongs
/// to <see cref="OverloadFailure"/>; the applicability and betterness rules stay here, since they are
/// what <see cref="Select"/> does rather than a separate concern it consults.
/// <para>
/// <see cref="ConversionRelation"/> is partial and documents the obligation that comes with it: it may
/// narrow and rank, never refuse. That bars selection from reading an eliminated candidate as proof
/// the call is wrong, but it does not extend to choosing one regardless: with every candidate
/// eliminated there is nothing to choose ON, so the answer is
/// <see cref="OverloadChoice{T}.Unresolvable"/> and the caller reports it. The two refusals built on
/// the relation, that and <see cref="ProgramModelDiagnostics.AmbiguousOverload"/>, are safe for the
/// reasons recorded on each.
/// </para>
/// <para>
/// A single closed candidate is returned without consulting the relation to select it. There is
/// nothing to select between, so applicability could only serve to refuse, and whether an argument
/// suits the one candidate able to take it is a type check rather than a selection.
/// </para>
/// <para>
/// A generic candidate is closed before any of that. Closing precedes selection because an open
/// parameter has no conversion into it, so a candidate left open would be eliminated for being generic
/// rather than for being wrong.
/// </para>
/// </summary>
internal static class OverloadResolution
{
    /// <param name="typeArguments">
    /// Null means infer. Empty is not the same: it admits only non-generic candidates.
    /// </param>
    public static OverloadChoice<T> Select<T>(
        IReadOnlyList<T> candidates,
        IReadOnlyList<Expression> arguments,
        ConstructedType? receiver,
        IReadOnlyList<IMistType>? typeArguments,
        TypeInterner interner,
        IReadOnlyList<bool>? argumentIsRef = null,
        IReadOnlyDictionary<T, IReadOnlyList<ConstructedType>>? paths = null,
        ILambdaTrials? lambdas = null,
        IContextualExpressionTrials? contextual = null)
        where T : class, IOverload
    {
        var receiverSubstitution = TypeSubstitution.ForReceiver(receiver);

        TypeSubstitution substitutionFor(T candidate)
        {
            return paths is { } reached && reached.TryGetValue(candidate, out var path)
                ? TypeSubstitution.ForPath(receiver, path, interner)
                : receiverSubstitution;
        }

        var byArity = new List<T>();
        foreach(var candidate in candidates)
        {
            var parameters = ValuePack.FlattenedSlot(candidate.ParametersList) is null
                ? candidate.ParametersList
                : ValuePack.Expand(candidate.ParametersList, substitutionFor(candidate), interner);

            if(ValuePack.AcceptsCount(parameters, arguments.Count))
            {
                byArity.Add(candidate);
            }
        }

        if(byArity.Count == 0)
        {
            return new OverloadChoice<T> { Selected = null, NoArityMatch = true };
        }

        if(typeArguments is not null)
        {
            var byTypeArity = byArity.FindAll(
                candidate => candidate.TypeParametersList.Count == typeArguments.Count);

            if(byTypeArity.Count == 0)
            {
                // byArity, not every candidate: one that cannot take this many arguments has no
                // type parameter count worth offering the user.
                return new OverloadChoice<T> { Selected = null, TypeArgumentMismatch = byArity };
            }

            byArity = byTypeArity;
        }

        var closed = new List<Applicant<T>>();
        var open = new List<T>();
        TypeArgumentConflict? openConflict = null;

        foreach(var candidate in byArity)
        {
            var substitution = Close(candidate, arguments, substitutionFor(candidate), typeArguments, interner, lambdas, out var conflict);

            if(substitution is null)
            {
                openConflict = open.Count == 0 ? conflict : openConflict;
                open.Add(candidate);
            }
            else
            {
                closed.Add(new Applicant<T>(
                    candidate, substitution, CallForm.Of(ValuePack.Expand(candidate.ParametersList, substitution, interner), arguments)));
            }
        }

        closed = WithoutHidden(closed, interner);

        if(closed.Count == 0)
        {
            return new OverloadChoice<T> { Selected = null, Uninferred = open, UninferredConflict = openConflict };
        }

        if(closed.Count == 1)
        {
            return Winner(closed[0]);
        }

        var applicable = closed.FindAll(candidate => IsApplicable(candidate, arguments, argumentIsRef, interner, lambdas, contextual));

        if(applicable.Count == 0)
        {
            // A lambda that binds under none of the candidates the other arguments admit is broken in
            // itself rather than evidence against one of them, so where those arguments leave exactly
            // one, it is chosen and binding the lambda against it reports what is wrong with the body.
            return (lambdas is not null || contextual is not null)
                && closed.FindAll(candidate => IsApplicable(candidate, arguments, argumentIsRef, interner, lambdas: null, contextual: null)) is [var only]
                    ? Winner(only)
                    : new OverloadChoice<T> { Selected = null, Unresolvable = Methods(closed) };
        }

        if(applicable.Count == 1)
        {
            return Winner(applicable[0]);
        }

        var maximal = Maximal(applicable, arguments, interner, lambdas);

        if(maximal.Count == 1 || !AreDistinguishable(Methods(maximal)))
        {
            return Winner(maximal[0]);
        }

        return new OverloadChoice<T> { Selected = null, Ambiguous = Methods(maximal) };
    }

    private sealed record Applicant<T>(T Method, TypeSubstitution Substitution, CallForm Form = default)
        where T : class, IOverload;

    /// <summary>
    /// One declaration per closed signature, and WHICH one is the language's rule rather than C#'s: a
    /// conformer's `Transform(Widget)` answers the slot it implements, `Transform(T)` at T = Widget, so
    /// a declaration beats an ABSTRACT's; but nothing may hide a class's member, so between two classes
    /// the INHERITED declaration stands and the deriving one -- which DeclarationShapeAnalyzer refuses where it
    /// is written -- answers nothing. Judged on the closed signatures, since two coincide only once the
    /// base's parameters are substituted. A later declaration repeating an earlier one's signature on
    /// the same type is likewise answered by the first.
    /// </summary>
    internal static List<Method> WithoutHidden(
        List<Method> candidates, Func<Method, TypeSubstitution> substitutionOf, TypeInterner interner)
    {
        var closed = candidates.ConvertAll(method => new Applicant<Method>(method, substitutionOf(method)));

        return WithoutHidden(closed, interner).ConvertAll(applicant => applicant.Method);
    }

    private static List<Applicant<T>> WithoutHidden<T>(List<Applicant<T>> closed, TypeInterner interner)
        where T : class, IOverload
    {
        if(closed.Count < 2)
        {
            return closed;
        }

        var keys = closed.ConvertAll(applicant => OverloadSignature.Of(applicant.Method, applicant.Substitution, interner));
        var kept = new List<Applicant<T>>();

        for(var i = 0; i < closed.Count; i++)
        {
            var hidden = false;

            for(var j = 0; j < closed.Count && !hidden; j++)
            {
                hidden = i != j
                    && keys[i] == keys[j]
                    && ((closed[i].Method.DeclaringType is AbstractType && Derives(closed[j].Method.DeclaringType, closed[i].Method.DeclaringType))
                        || (closed[j].Method.DeclaringType is not AbstractType && Derives(closed[i].Method.DeclaringType, closed[j].Method.DeclaringType))
                        || (j < i
                            && ReferenceEquals(closed[i].Method.DeclaringType, closed[j].Method.DeclaringType)
                            && OverloadSignature.Repeats(closed[i].Method, closed[j].Method)));
            }

            if(!hidden)
            {
                kept.Add(closed[i]);
            }
        }

        return kept;
    }

    private static bool Derives(MemberType? derived, MemberType? ancestor)
    {
        if(derived is null || ancestor is null || ReferenceEquals(derived, ancestor))
        {
            return false;
        }

        var pending = new Queue<MistType>();
        var seen = new HashSet<MistType>();
        pending.Enqueue(derived);

        while(pending.Count > 0)
        {
            var current = pending.Dequeue();
            var definition = current is ConstructedType constructed ? constructed.Definition as MistType : current;

            if(definition is null || !seen.Add(definition))
            {
                continue;
            }

            if(ReferenceEquals(definition, ancestor))
            {
                return true;
            }

            if(definition is ConcreteType { BaseType: { } baseType })
            {
                pending.Enqueue(baseType);
            }

            if(definition is MemberType member)
            {
                foreach(var abstractBase in member.BaseAbstractTypes)
                {
                    pending.Enqueue(abstractBase);
                }
            }

            if(definition is ConcreteType concrete)
            {
                foreach(var conformance in concrete.ConformancesList)
                {
                    if(conformance.AbstractType is MistType conformed)
                    {
                        pending.Enqueue(conformed);
                    }
                }
            }
        }

        return false;
    }

    private static OverloadChoice<T> Winner<T>(Applicant<T> applicant)
        where T : class, IOverload
    {
        return new OverloadChoice<T> { Selected = applicant.Method, Substitution = applicant.Substitution };
    }

    private static List<T> Methods<T>(List<Applicant<T>> applicants)
        where T : class, IOverload
    {
        return applicants.ConvertAll(applicant => applicant.Method);
    }

    /// <summary>
    /// Null if any of the candidate's own type parameters went undetermined.
    /// </summary>
    private static TypeSubstitution? Close<T>(
        T candidate,
        IReadOnlyList<Expression> arguments,
        TypeSubstitution receiver,
        IReadOnlyList<IMistType>? typeArguments,
        TypeInterner interner,
        ILambdaTrials? lambdas,
        out TypeArgumentConflict? conflict)
        where T : class, IOverload
    {
        conflict = null;

        if(candidate.TypeParametersList.Count == 0)
        {
            return receiver;
        }

        var own = typeArguments is null
            ? TypeSubstitution.ForMethod(candidate, TypeArgumentInference.Infer(candidate, arguments, receiver, interner, lambdas, out conflict))
            : TypeSubstitution.ForMethod(candidate, typeArguments)
            ;

        var substitution = receiver.Extend(own);

        return substitution.Unbound(candidate.TypeParametersList).Count == 0 ? substitution : null;
    }

    /// <summary>
    /// The candidates no other candidate beats. One means a winner; several mean a tie.
    /// <para>
    /// Every candidate is compared against every other rather than against a running winner, because
    /// betterness is a partial order: A can beat the incumbent without beating the candidate it never
    /// met. A running scan would leave beaten candidates in the reported tie -- one <c>int</c> against
    /// <c>decimal</c>, <c>float</c> and <c>double</c> would name all three, though <c>float</c> beats
    /// <c>double</c>.
    /// </para>
    /// </summary>
    private static List<Applicant<T>> Maximal<T>(
        List<Applicant<T>> applicable, IReadOnlyList<Expression> arguments, TypeInterner interner, ILambdaTrials? lambdas)
        where T : class, IOverload
    {
        var maximal = new List<Applicant<T>>();

        foreach(var candidate in applicable)
        {
            var beaten = false;

            foreach(var other in applicable)
            {
                if(!ReferenceEquals(other, candidate) && IsBetter(other, candidate, arguments, interner, lambdas))
                {
                    beaten = true;
                    break;
                }
            }

            if(!beaten)
            {
                maximal.Add(candidate);
            }
        }

        return maximal;
    }

    /// <summary>
    /// Whether the tied candidates can be told apart in a message. They cannot when two render
    /// identically, which means <see cref="ConversionRelation.IsIdentity"/> failed to separate types
    /// that a fully qualified name does not distinguish -- a lifted value's type among them. Refusing
    /// a call on that basis would report a tie between two signatures the user sees as one, so the
    /// call is bound instead.
    /// </summary>
    private static bool AreDistinguishable<T>(List<T> tied)
        where T : class, IOverload
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach(var candidate in tied)
        {
            if(!seen.Add(OverloadFailure.DescribeSignatures(new[] { candidate })))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// PASSING MODE IS PART OF APPLICABILITY, not decoration on top of it. A <c>ref</c> argument and a
    /// by-value one are different arguments even when their types agree, so a by-ref and a by-value
    /// overload of one name are only distinguishable here. Without this both bound to the by-value
    /// candidate, and a program calling either printed the by-value answer with no diagnostic.
    /// <para>
    /// The modes must MATCH rather than merely be compatible: C# refuses a by-value argument to a
    /// <c>ref</c> parameter (CS1620) as firmly as the reverse, and Mist's own grammar makes the call
    /// site say which it means.
    /// </para>
    /// <para>
    /// Flags are optional because a caller without an argument list has nothing to read them from. A
    /// caller that omits them gets the type-only behaviour, which is what a lone candidate would have
    /// received anyway -- applicability is consulted only from two candidates up.
    /// </para>
    /// </summary>
    private static bool IsApplicable<T>(
        Applicant<T> candidate,
        IReadOnlyList<Expression> arguments,
        IReadOnlyList<bool>? argumentIsRef,
        TypeInterner interner,
        ILambdaTrials? lambdas,
        IContextualExpressionTrials? contextual)
        where T : class, IOverload
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(argumentIsRef is not null
                && i < argumentIsRef.Count
                && argumentIsRef[i] != candidate.Method.ParametersList[ValuePack.SlotOf(candidate.Method.ParametersList, arguments.Count, i)].Modifier.IsRef)
            {
                return false;
            }

            var parameter = ParameterType(candidate, i, arguments, interner);

            // A method group has no type to convert FROM, so applicability is the conversion itself:
            // whether one of its overloads is the function this parameter asks for. Asked here because
            // it is the only question that can tell two arity-matching candidates apart when the
            // argument is a group -- which is the whole reason the group was left unsettled.
            if(arguments[i] is MethodGroupExpression group)
            {
                if(!MethodGroupConversion.Accepts(group, parameter, interner))
                {
                    return false;
                }

                continue;
            }

            if(arguments[i] is UnboundContextualExpression expression)
            {
                if(contextual is not null && !contextual.Fits(expression, parameter))
                {
                    return false;
                }

                continue;
            }

            if(arguments[i] is UnboundLambdaExpression lambda)
            {
                if(lambdas is not null && !lambdas.Fits(lambda, parameter))
                {
                    return false;
                }

                continue;
            }

            if(!ConversionRelation.IsImplicit(arguments[i].Type, parameter) && !ConstantConversion.Fits(arguments[i], parameter))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="left"/> beats <paramref name="right"/>: no argument converts worse to
    /// it, and at least one converts better. Both are already applicable, so this only orders them.
    /// <para>
    /// A tie no argument separates is settled by <see cref="BeatsOnSignature"/>.
    /// </para>
    /// </summary>
    private static bool IsBetter<T>(
        Applicant<T> left, Applicant<T> right, IReadOnlyList<Expression> arguments, TypeInterner interner, ILambdaTrials? lambdas)
        where T : class, IOverload
    {
        var strictlyBetterSomewhere = false;

        for(var i = 0; i < arguments.Count; i++)
        {
            var leftParameter = ParameterType(left, i, arguments, interner);
            var rightParameter = ParameterType(right, i, arguments, interner);
            var argument = arguments[i] is UnboundLambdaExpression lambda && lambdas is not null
                ? lambdas.ComparedType(lambda, leftParameter, rightParameter)
                : arguments[i].Type;

            var comparison = CompareConversions(argument, leftParameter, rightParameter);

            if(comparison > 0)
            {
                return false;
            }

            if(comparison < 0)
            {
                strictlyBetterSomewhere = true;
            }
        }

        return strictlyBetterSomewhere || BeatsOnSignature(left, right, arguments, interner);
    }

    /// <summary>
    /// The tiebreak between candidates no argument separates: A PARAMETER FIXED AT THE DECLARATION BEATS
    /// ONE THE RECEIVER'S TYPE ARGUMENT SUPPLIES. `this[int i]` takes an int in every instantiation,
    /// where `this[K key]` takes whatever that instantiation gives K, so at K = int the two coincide and
    /// the fixed one is the narrower intent. Fixed all the way down, so `Take(int[])` beats `Take(K[])`;
    /// and a candidate declaring no type parameters of its own is settled first, before the parameters
    /// are compared at all. Neither side is fixed in `Take(A)` beside `Take(B)`, nor in `Take(K, int)`
    /// beside `Take(int, K)`, which stay ambiguous.
    /// <para>
    /// ONLY ON IDENTICAL CLOSED PARAMETER TYPES, which is what keeps this a tiebreak rather than a second
    /// ranking: without that gate one position's genericity would settle a tie in another, and
    /// `Take(short, int)` would beat a generic `Take(uint, T)` for a byte argument, though
    /// method-overloading Requirement 3.3 gives a signed and an unsigned target no preference. Refusing
    /// the coinciding pair instead left neither callable, no cast telling them apart, since the cast's
    /// own type is what they already share.
    /// </para>
    /// </summary>
    private static bool BeatsOnSignature<T>(
        Applicant<T> left, Applicant<T> right, IReadOnlyList<Expression> arguments, TypeInterner interner)
        where T : class, IOverload
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(!ConversionRelation.IsIdentity(ParameterType(left, i, arguments, interner), ParameterType(right, i, arguments, interner)))
            {
                return false;
            }
        }

        if((left.Method.TypeParametersList.Count > 0) == (right.Method.TypeParametersList.Count > 0)
            && CallForm.Prefer(left.Form, right.Form) is var form and not 0)
        {
            return form < 0;
        }

        return MoreSpecificSignature(left.Method, right.Method, arguments.Count);
    }

    internal static bool MoreSpecificSignature(IOverload left, IOverload right, int count)
    {
        var leftIsGeneric = left.TypeParametersList.Count > 0;
        var rightIsGeneric = right.TypeParametersList.Count > 0;

        if(leftIsGeneric != rightIsGeneric)
        {
            return rightIsGeneric;
        }

        var moreSpecificSomewhere = false;

        for(var i = 0; i < count; i++)
        {
            var leftSlot = ValuePack.SlotOf(left.ParametersList, count, i);
            var rightSlot = ValuePack.SlotOf(right.ParametersList, count, i);
            var comparison = CompareSpecificity(left.ParametersList[leftSlot].Type, right.ParametersList[rightSlot].Type);

            if(comparison > 0)
            {
                return false;
            }

            if(comparison < 0)
            {
                moreSpecificSomewhere = true;
            }
        }

        return moreSpecificSomewhere;
    }

    private static int CompareSpecificity(IMistType left, IMistType right)
    {
        var leftIsOpen = left is ITypeParameter;
        var rightIsOpen = right is ITypeParameter;

        if(leftIsOpen != rightIsOpen)
        {
            return leftIsOpen ? 1 : -1;
        }

        if(leftIsOpen)
        {
            return 0;
        }

        if(left is IConstructedType leftConstructed
            && right is IConstructedType rightConstructed
            && ConversionRelation.IsIdentity(leftConstructed.Definition, rightConstructed.Definition))
        {
            return CompareParts(leftConstructed.TypeArguments, rightConstructed.TypeArguments);
        }

        if(left is ITupleType leftTuple && right is ITupleType rightTuple)
        {
            return CompareParts(leftTuple.ElementTypes, rightTuple.ElementTypes);
        }

        if(OptionalUnion.InnerType(left) is { } leftInner && OptionalUnion.InnerType(right) is { } rightInner)
        {
            return CompareSpecificity(leftInner, rightInner);
        }

        if(left is IFunctionType leftFunction && right is IFunctionType rightFunction)
        {
            return CompareParts(WithResult(leftFunction), WithResult(rightFunction));
        }

        return 0;
    }

    private static List<IMistType> WithResult(IFunctionType function)
    {
        var parts = new List<IMistType>(function.ParameterTypes) { function.ReturnType };

        return parts;
    }

    private static int CompareParts(IReadOnlyList<IMistType> left, IReadOnlyList<IMistType> right)
    {
        if(left.Count != right.Count)
        {
            return 0;
        }

        var leftIsMoreSpecific = false;
        var rightIsMoreSpecific = false;

        for(var i = 0; i < left.Count; i++)
        {
            var comparison = CompareSpecificity(left[i], right[i]);
            leftIsMoreSpecific |= comparison < 0;
            rightIsMoreSpecific |= comparison > 0;
        }

        if(leftIsMoreSpecific == rightIsMoreSpecific)
        {
            return 0;
        }

        return leftIsMoreSpecific ? -1 : 1;
    }

    /// <summary>
    /// Which of two parameter types is the better destination for the same argument. Negative for
    /// <paramref name="left"/>, positive for <paramref name="right"/>, zero when neither is better.
    /// <para>
    /// Exact beats converted. Otherwise the narrower target wins, established by asking which CONTAINS
    /// the other rather than which is reachable from it -- see <see cref="ConversionRelation.IsWithin"/>.
    /// Two mutually uncontained targets tie, which is what an ambiguous call is made of, and DIVERGES
    /// FROM CLASSIC where a user-declared operator between them resolves the call instead. A signed and
    /// an unsigned integral target tie too, with no preference between them, as classic reports them
    /// (method-overloading Requirement 3.3).
    /// </para>
    /// </summary>
    internal static int CompareConversions(IMistType argument, IMistType left, IMistType right)
    {
        if(ConversionRelation.IsIdentity(left, right))
        {
            return 0;
        }

        var leftIsExact = ConversionRelation.IsIdentity(argument, left);
        var rightIsExact = ConversionRelation.IsIdentity(argument, right);

        if(leftIsExact != rightIsExact)
        {
            return leftIsExact ? -1 : 1;
        }

        var leftToRight = ConversionRelation.IsWithin(left, right);
        var rightToLeft = ConversionRelation.IsWithin(right, left);

        return leftToRight == rightToLeft ? 0 : leftToRight ? -1 : 1;
    }

    private static IMistType ParameterType<T>(Applicant<T> candidate, int index, IReadOnlyList<Expression>? arguments, TypeInterner interner)
        where T : class, IOverload
    {
        var parameters = ValuePack.Expand(candidate.Method.ParametersList, candidate.Substitution, interner);
        var count = arguments?.Count ?? parameters.Count;
        var parameter = parameters[ValuePack.SlotOf(parameters, count, index)];
        var target = ValuePack.ElementOf(parameter) is { } element && candidate.Form.Expanded ? element : parameter.Type;

        return candidate.Substitution.Apply(target, interner);
    }
}