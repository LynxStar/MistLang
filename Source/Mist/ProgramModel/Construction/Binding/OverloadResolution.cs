using Mist.Common.Diagnostics;
using Mist.ProgramModel.Construction.Resolution;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a call's arguments picked out of its candidates. A null <see cref="Selected"/> leaves exactly
/// one of the five failure members non-empty, since each carries a different diagnostic.
/// </summary>
internal sealed class OverloadChoice<T>
    where T : Method
{
    public required T? Selected { get; init; }

    public bool NoArityMatch { get; init; }

    /// <summary>
    /// The receiver's type arguments and the method's own, together. <see cref="Selected"/> is the
    /// declaration, so its signature has to be read through this.
    /// </summary>
    public TypeSubstitution Substitution { get; init; } = TypeSubstitution.Empty;

    /// <summary>
    /// The candidates that tied, empty unless the call is ambiguous. Each is applicable and none is
    /// beaten by another, so choosing between them would be arbitrary.
    /// </summary>
    public IReadOnlyList<T> Ambiguous { get; init; } = [];

    /// <summary>
    /// The arity matches, empty unless every one of them failed applicability. Distinct from
    /// <see cref="Ambiguous"/> in what it rests on: a tie is several positive results, this is none.
    /// </summary>
    public IReadOnlyList<T> Unresolvable { get; init; } = [];

    /// <summary>
    /// The arity matches, empty unless every one of them is generic and left a type parameter
    /// undetermined.
    /// </summary>
    public IReadOnlyList<T> Uninferred { get; init; } = [];

    /// <summary>
    /// The arity matches, empty unless the call stated type arguments and no candidate declares that
    /// many type parameters.
    /// </summary>
    public IReadOnlyList<T> TypeArgumentMismatch { get; init; } = [];
}

/// <summary>
/// Chooses which overload a call selects, in C#'s three stages: filter by arity, keep the candidates
/// every argument is convertible to, then take the best of those.
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
        TypeInterner interner)
        where T : Method
    {
        var receiverSubstitution = TypeSubstitution.ForReceiver(receiver);

        var byArity = new List<T>();
        foreach(var candidate in candidates)
        {
            if(candidate.ParametersList.Count == arguments.Count)
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

        foreach(var candidate in byArity)
        {
            var substitution = Close(candidate, arguments, receiverSubstitution, typeArguments, interner);

            if(substitution is null)
            {
                open.Add(candidate);
            }
            else
            {
                closed.Add(new Applicant<T>(candidate, substitution));
            }
        }

        if(closed.Count == 0)
        {
            return new OverloadChoice<T> { Selected = null, Uninferred = open };
        }

        if(closed.Count == 1)
        {
            return Winner(closed[0]);
        }

        var applicable = new List<Applicant<T>>();
        foreach(var candidate in closed)
        {
            if(IsApplicable(candidate, arguments, interner))
            {
                applicable.Add(candidate);
            }
        }

        if(applicable.Count == 0)
        {
            return new OverloadChoice<T> { Selected = null, Unresolvable = Methods(closed) };
        }

        if(applicable.Count == 1)
        {
            return Winner(applicable[0]);
        }

        var maximal = Maximal(applicable, arguments, interner);

        if(maximal.Count == 1 || !AreDistinguishable(Methods(maximal)))
        {
            return Winner(maximal[0]);
        }

        return new OverloadChoice<T> { Selected = null, Ambiguous = Methods(maximal) };
    }

    private sealed record Applicant<T>(T Method, TypeSubstitution Substitution)
        where T : Method;

    private static OverloadChoice<T> Winner<T>(Applicant<T> applicant)
        where T : Method
    {
        return new OverloadChoice<T> { Selected = applicant.Method, Substitution = applicant.Substitution };
    }

    private static List<T> Methods<T>(List<Applicant<T>> applicants)
        where T : Method
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
        TypeInterner interner)
        where T : Method
    {
        if(candidate.TypeParametersList.Count == 0)
        {
            return receiver;
        }

        var own = typeArguments is null
            ? TypeSubstitution.ForMethod(candidate, TypeArgumentInference.Infer(candidate, arguments, receiver, interner))
            : TypeSubstitution.ForMethod(candidate, typeArguments)
            ;

        var substitution = receiver.Extend(own);

        return substitution.Unbound(candidate.TypeParametersList).Count == 0 ? substitution : null;
    }

    /// <summary>
    /// Reports whichever failure a null <see cref="OverloadChoice{T}.Selected"/> stands for. Called
    /// only where selection failed; with a winner in hand there is nothing here to say.
    /// <para>
    /// Lives here rather than at the two call sites because the states have to be told apart together.
    /// <see cref="OverloadChoice{T}.Ambiguous"/> is empty for every other failure, so a site testing
    /// only some of them reports a tie between no candidates at all.
    /// </para>
    /// </summary>
    /// <param name="description">
    /// Names the callee, and appears mid-sentence in every message, so it is lower case and carries
    /// its own kind: <c>method 'Format'</c>.
    /// </param>
    public static void ReportFailure<T>(
        OverloadChoice<T> choice,
        IReadOnlyList<T> candidates,
        int argumentCount,
        string description,
        Storm.Runtime.SyntaxNode node,
        DiagnosticBag diagnostics)
        where T : Method
    {
        if(choice.NoArityMatch)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.NoApplicableOverload, node, null,
                description, argumentCount, DescribeArities(candidates));

            return;
        }

        if(choice.TypeArgumentMismatch.Count > 0)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TypeArgumentCountMismatch, node, null,
                description, DescribeTypeArities(choice.TypeArgumentMismatch));

            return;
        }

        if(choice.Uninferred.Count > 0)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.TypeArgumentsNotInferred, node, null,
                description, DescribeTypeParameters(choice.Uninferred));

            return;
        }

        if(choice.Unresolvable.Count > 0)
        {
            diagnostics.ReportAtNode(
                ProgramModelDiagnostics.UnresolvableOverload, node, null,
                description, DescribeSignatures(choice.Unresolvable));

            return;
        }

        diagnostics.ReportAtNode(
            ProgramModelDiagnostics.AmbiguousOverload, node, null,
            description, DescribeSignatures(choice.Ambiguous));
    }

    private static string DescribeTypeArities<T>(IReadOnlyList<T> candidates)
        where T : Method
    {
        var arities = new SortedSet<int>();
        foreach(var candidate in candidates)
        {
            _ = arities.Add(candidate.TypeParametersList.Count);
        }

        return string.Join(", ", arities);
    }

    /// <summary>
    /// Every type parameter the first candidate declares, determined or not, because a call states all
    /// of a method's type arguments or none and so all of them are what the user has to write. Which
    /// ones went undetermined is therefore not worth carrying out of selection.
    /// </summary>
    private static string DescribeTypeParameters<T>(IReadOnlyList<T> candidates)
        where T : Method
    {
        var names = candidates[0].TypeParametersList.Select(parameter => $"'{parameter.Name}'");

        return string.Join(", ", names);
    }

    /// <summary>
    /// The parameter counts on offer, ascending and deduplicated, for a diagnostic that would
    /// otherwise say the call was wrong without saying what would be right.
    /// </summary>
    public static string DescribeArities<T>(IReadOnlyList<T> candidates)
        where T : Method
    {
        var arities = new SortedSet<int>();
        foreach(var candidate in candidates)
        {
            _ = arities.Add(candidate.ParametersList.Count);
        }

        return string.Join(", ", arities);
    }

    /// <summary>
    /// The tied candidates as parameter lists, spelled the way source spells types, so the reader can
    /// match them against what they wrote.
    /// </summary>
    public static string DescribeSignatures<T>(IReadOnlyList<T> candidates)
        where T : Method
    {
        var signatures = candidates
            .Select(c => $"({string.Join(", ", c.ParametersList.Select(p => TypeDisplay.Mist(p.Type)))})");

        return string.Join(", ", signatures);
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
        List<Applicant<T>> applicable, IReadOnlyList<Expression> arguments, TypeInterner interner)
        where T : Method
    {
        var maximal = new List<Applicant<T>>();

        foreach(var candidate in applicable)
        {
            var beaten = false;

            foreach(var other in applicable)
            {
                if(!ReferenceEquals(other, candidate) && IsBetter(other, candidate, arguments, interner))
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
        where T : Method
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach(var candidate in tied)
        {
            if(!seen.Add(DescribeSignatures(new[] { candidate })))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsApplicable<T>(Applicant<T> candidate, IReadOnlyList<Expression> arguments, TypeInterner interner)
        where T : Method
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(!ConversionRelation.IsImplicit(arguments[i].Type, ParameterType(candidate, i, interner)))
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
    /// A non-generic candidate wins an otherwise-exact tie, as it does in C#: closing a generic
    /// candidate can make its parameter types identical to a non-generic sibling's, and the call would
    /// then be refused as ambiguous between a method and its own specialisation.
    /// </para>
    /// </summary>
    private static bool IsBetter<T>(
        Applicant<T> left, Applicant<T> right, IReadOnlyList<Expression> arguments, TypeInterner interner)
        where T : Method
    {
        var strictlyBetterSomewhere = false;

        for(var i = 0; i < arguments.Count; i++)
        {
            var comparison = CompareConversions(
                arguments[i].Type, ParameterType(left, i, interner), ParameterType(right, i, interner));

            if(comparison > 0)
            {
                return false;
            }

            if(comparison < 0)
            {
                strictlyBetterSomewhere = true;
            }
        }

        return strictlyBetterSomewhere
            || (left.Method.TypeParametersList.Count == 0 && right.Method.TypeParametersList.Count > 0)
            ;
    }

    /// <summary>
    /// Which of two parameter types is the better destination for the same argument. Negative for
    /// <paramref name="left"/>, positive for <paramref name="right"/>, zero when neither is better.
    /// <para>
    /// Exact beats converted. Otherwise the narrower target wins, established by asking which
    /// direction converts: <c>int</c> beats <c>long</c> because int widens to long and long does not
    /// narrow to int. Two mutually unconvertible targets tie, which is what an ambiguous call is
    /// made of.
    /// </para>
    /// </summary>
    private static int CompareConversions(IMistType argument, IMistType left, IMistType right)
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

        // Which target contains the other, not which is reachable from it -- see
        // ConversionRelation.IsWithin. DIVERGES FROM CLASSIC, which resolves such a call rather than
        // refusing it.
        var leftToRight = ConversionRelation.IsWithin(left, right);
        var rightToLeft = ConversionRelation.IsWithin(right, left);

        if(leftToRight == rightToLeft)
        {
            return 0;
        }

        return leftToRight ? -1 : 1;
    }

    private static IMistType ParameterType<T>(Applicant<T> candidate, int index, TypeInterner interner)
        where T : Method
    {
        return candidate.Substitution.Apply(candidate.Method.ParametersList[index].Type, interner);
    }
}