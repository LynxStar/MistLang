using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Analysis.Builtin;

/// <summary>
/// The outcome of overload resolution for a method or constructor call site.
/// </summary>
public enum OverloadResolutionStatus
{
    /// <summary>A single best candidate was found.</summary>
    Success,

    /// <summary>No candidate's parameter signature matched the argument types.</summary>
    NoApplicableCandidate,

    /// <summary>Two or more candidates are equally applicable with no single best match.</summary>
    AmbiguousCall,
}

/// <summary>
/// The result of overload resolution, containing the status and either the best candidate
/// or the set of ambiguous candidates.
/// </summary>
public readonly record struct OverloadResolutionResult(
    OverloadResolutionStatus Status,
    ISymbol? BestCandidate,
    IReadOnlyList<ISymbol>? AmbiguousCandidates);

/// <summary>
/// Resolves the best-matching method or constructor from an overload set
/// given the argument types at a call site. This is a pure static class
/// with no mutable state, designed to be testable in isolation.
/// </summary>
public static class OverloadResolver
{
    /// <summary>
    /// Selects the best candidate from <paramref name="candidates"/> whose parameter
    /// signature matches <paramref name="argumentTypes"/>.
    /// </summary>
    /// <param name="candidates">All symbols sharing the same name (the overload set).</param>
    /// <param name="argumentTypes">The resolved types of the arguments at the call site.</param>
    /// <param name="model">The semantic model, used to extract parameter signatures.</param>
    /// <returns>A resolution result indicating success, no match, or ambiguity.</returns>
    public static OverloadResolutionResult Resolve(
        IReadOnlyList<ISymbol> candidates,
        IReadOnlyList<IType> argumentTypes,
        ISemanticModel model)
    {
        // Step 1: Filter to applicable candidates — parameter count must match
        // and each argument type must be assignable to the corresponding parameter type.
        var applicable = new List<(ISymbol Symbol, IReadOnlyList<IType> ParamTypes)>();

        foreach (var candidate in candidates)
        {
            var paramTypes = ParameterSignature.Extract(candidate, model);

            if (paramTypes.Count != argumentTypes.Count)
                continue;

            var allAssignable = true;
            for (var i = 0; i < argumentTypes.Count; i++)
            {
                if (!TypeCheckerAnalyzer.IsAssignable(argumentTypes[i], paramTypes[i]))
                {
                    allAssignable = false;
                    break;
                }
            }

            if (allAssignable)
                applicable.Add((candidate, paramTypes));
        }

        // Step 2: No applicable candidates.
        if (applicable.Count == 0)
        {
            return new OverloadResolutionResult(
                OverloadResolutionStatus.NoApplicableCandidate,
                BestCandidate: null,
                AmbiguousCandidates: null);
        }

        // Step 3: Exactly one applicable candidate.
        if (applicable.Count == 1)
        {
            return new OverloadResolutionResult(
                OverloadResolutionStatus.Success,
                BestCandidate: applicable[0].Symbol,
                AmbiguousCandidates: null);
        }

        // Step 4: Multiple applicable candidates — find the most specific.
        var best = applicable[0];
        var bestIsValid = true;

        for (var i = 1; i < applicable.Count; i++)
        {
            if (!bestIsValid)
                break;

            if (MoreSpecific(applicable[i].ParamTypes, best.ParamTypes))
            {
                // applicable[i] is more specific than current best — it becomes the new best.
                best = applicable[i];
            }
            else if (!MoreSpecific(best.ParamTypes, applicable[i].ParamTypes))
            {
                // Neither is more specific than the other — potential ambiguity.
                bestIsValid = false;
            }
        }

        if (bestIsValid)
        {
            // Step 5: Verify best is more specific than ALL other applicable candidates.
            foreach (var other in applicable)
            {
                if (ReferenceEquals(other.Symbol, best.Symbol))
                    continue;

                if (!MoreSpecific(best.ParamTypes, other.ParamTypes))
                {
                    // best is not more specific than this other candidate — ambiguous.
                    return new OverloadResolutionResult(
                        OverloadResolutionStatus.AmbiguousCall,
                        BestCandidate: null,
                        AmbiguousCandidates: applicable.Select(a => a.Symbol).ToList());
                }
            }

            return new OverloadResolutionResult(
                OverloadResolutionStatus.Success,
                BestCandidate: best.Symbol,
                AmbiguousCandidates: null);
        }

        // No single most-specific candidate — ambiguous call.
        return new OverloadResolutionResult(
            OverloadResolutionStatus.AmbiguousCall,
            BestCandidate: null,
            AmbiguousCandidates: applicable.Select(a => a.Symbol).ToList());
    }

    /// <summary>
    /// Determines whether candidate A is more specific than candidate B.
    /// A is more specific than B if:
    /// - For all parameter positions, A's type is assignable to B's type
    /// - For at least one position, B's type is NOT assignable to A's type
    /// </summary>
    private static bool MoreSpecific(IReadOnlyList<IType> aParams, IReadOnlyList<IType> bParams)
    {
        if (aParams.Count != bParams.Count)
            return false;

        var allAtoB = true;
        var anyBnotToA = false;

        for (var i = 0; i < aParams.Count; i++)
        {
            if (!TypeCheckerAnalyzer.IsAssignable(aParams[i], bParams[i]))
                allAtoB = false;

            if (!TypeCheckerAnalyzer.IsAssignable(bParams[i], aParams[i]))
                anyBnotToA = true;
        }

        return allAtoB && anyBnotToA;
    }
}
