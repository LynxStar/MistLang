using Mist.Common.Diagnostics;
using Mist.ProgramModel.Contracts.Types;
using Mist.ProgramModel.Model.Expressions;
using Mist.ProgramModel.Model.Members;
using Mist.ProgramModel.Model.Types;

namespace Mist.ProgramModel.Construction.Binding;

/// <summary>
/// What a call's arguments picked out of its candidates.
/// <para>
/// <see cref="Selected"/> is null in exactly three cases, and the caller must tell them apart
/// because they carry different diagnostics: no candidate takes this many arguments
/// (<see cref="NoArityMatch"/>), several tied (<see cref="Ambiguous"/>), or none could be placed at
/// all (<see cref="Unresolvable"/>).
/// </para>
/// </summary>
internal sealed class OverloadChoice<T>
    where T : Method
{
    public required T? Selected { get; init; }

    public bool NoArityMatch { get; init; }

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
/// A single arity match is returned without consulting the relation at all. There is nothing to
/// select between, so applicability could only serve to refuse, and whether an argument suits the one
/// candidate able to take it is a type check rather than a selection.
/// </para>
/// </summary>
internal static class OverloadResolution
{
    public static OverloadChoice<T> Select<T>(
        IReadOnlyList<T> candidates,
        IReadOnlyList<Expression> arguments,
        ConstructedType? receiver)
        where T : Method
    {
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

        if(byArity.Count == 1)
        {
            return new OverloadChoice<T> { Selected = byArity[0] };
        }

        var applicable = new List<T>();
        foreach(var candidate in byArity)
        {
            if(IsApplicable(candidate, arguments, receiver))
            {
                applicable.Add(candidate);
            }
        }

        if(applicable.Count == 0)
        {
            return new OverloadChoice<T> { Selected = null, Unresolvable = byArity };
        }

        if(applicable.Count == 1)
        {
            return new OverloadChoice<T> { Selected = applicable[0] };
        }

        var maximal = Maximal(applicable, arguments, receiver);

        if(maximal.Count == 1 || !AreDistinguishable(maximal))
        {
            return new OverloadChoice<T> { Selected = maximal[0] };
        }

        return new OverloadChoice<T> { Selected = null, Ambiguous = maximal };
    }

    /// <summary>
    /// Reports whichever failure a null <see cref="OverloadChoice{T}.Selected"/> stands for. Called
    /// only where selection failed; with a winner in hand there is nothing here to say.
    /// <para>
    /// Lives here rather than at the two call sites because the three states have to be told apart
    /// together. <see cref="OverloadChoice{T}.Ambiguous"/> is empty for an unresolvable call, so a
    /// site testing only two of them reports a tie between no candidates at all.
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
    private static List<T> Maximal<T>(
        List<T> applicable, IReadOnlyList<Expression> arguments, ConstructedType? receiver)
        where T : Method
    {
        var maximal = new List<T>();

        foreach(var candidate in applicable)
        {
            var beaten = false;

            foreach(var other in applicable)
            {
                if(!ReferenceEquals(other, candidate) && IsBetter(other, candidate, arguments, receiver))
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

    private static bool IsApplicable<T>(T candidate, IReadOnlyList<Expression> arguments, ConstructedType? receiver)
        where T : Method
    {
        for(var i = 0; i < arguments.Count; i++)
        {
            if(!ConversionRelation.IsImplicit(arguments[i].Type, ParameterType(candidate, i, receiver)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Whether <paramref name="left"/> beats <paramref name="right"/>: no argument converts worse to
    /// it, and at least one converts better. Both are already applicable, so this only orders them.
    /// </summary>
    private static bool IsBetter<T>(
        T left, T right, IReadOnlyList<Expression> arguments, ConstructedType? receiver)
        where T : Method
    {
        var strictlyBetterSomewhere = false;

        for(var i = 0; i < arguments.Count; i++)
        {
            var comparison = CompareConversions(
                arguments[i].Type, ParameterType(left, i, receiver), ParameterType(right, i, receiver));

            if(comparison > 0)
            {
                return false;
            }

            if(comparison < 0)
            {
                strictlyBetterSomewhere = true;
            }
        }

        return strictlyBetterSomewhere;
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

        var leftToRight = ConversionRelation.IsImplicit(left, right);
        var rightToLeft = ConversionRelation.IsImplicit(right, left);

        if(leftToRight == rightToLeft)
        {
            return 0;
        }

        return leftToRight ? -1 : 1;
    }

    /// <summary>
    /// A parameter's type as seen at the call. The overloads are declared on the generic definition,
    /// so a parameter stated in its type parameters means nothing until the receiver's arguments are
    /// substituted in.
    /// </summary>
    private static IMistType ParameterType<T>(T candidate, int index, ConstructedType? receiver)
        where T : Method
    {
        var declared = candidate.ParametersList[index].Type;
        return receiver?.Substitute(declared) ?? declared;
    }
}