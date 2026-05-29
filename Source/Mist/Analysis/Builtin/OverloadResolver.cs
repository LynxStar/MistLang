using System.Linq;
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
    /// <param name="candidateOrigin">
    /// Optional origin-vector tiebreaker for operator overload resolution.
    /// Must be <c>null</c> or have the same length as <paramref name="candidates"/>.
    /// When provided, a lower origin value wins over a higher one in an
    /// otherwise-ambiguous result (see design § 5). For binary operators
    /// <c>ResolveBinaryOperator</c> uses <c>0</c> for candidates gathered from
    /// the left operand's type and <c>1</c> for those gathered from the right.
    /// Method-call call sites pass <c>null</c> and preserve the pre-tiebreaker
    /// behavior. The tiebreaker resolves ambiguity only — it does not change
    /// applicability filtering or the <see cref="MoreSpecific"/> relation.
    /// </param>
    /// <param name="argumentRefKinds">
    /// Optional ref kinds for each argument at the call site. When provided,
    /// candidates whose parameter RefKind values do not match the argument
    /// RefKind values are filtered out during applicability checking (Requirement 7.4).
    /// </param>
    /// <param name="inferenceResults">
    /// Optional inference results parallel to <paramref name="candidates"/>.
    /// When provided, candidates at index <c>i</c> where <c>inferenceResults[i]</c>
    /// is non-null and <c>Success == false</c> are excluded from the applicable set.
    /// This enables inference-aware overload resolution: when type arguments are
    /// omitted, the caller attempts inference for each generic candidate and passes
    /// the results here so that candidates with failed inference are filtered out
    /// before specificity ranking (Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.6).
    /// </param>
    /// <returns>A resolution result indicating success, no match, or ambiguity.</returns>
    public static OverloadResolutionResult Resolve(
        IReadOnlyList<ISymbol> candidates,
        IReadOnlyList<IType> argumentTypes,
        ISemanticModel model,
        IReadOnlyList<int>? candidateOrigin = null,
        (IReadOnlyList<IType> TypeParams, IReadOnlyList<IType> TypeArgs)? typeSubstitution = null,
        IReadOnlyList<RefKind>? argumentRefKinds = null,
        IReadOnlyList<InferenceResult?>? inferenceResults = null)
    {
        // Validate origin vector length — callers must supply one entry per
        // candidate when they opt into the tiebreaker. Mismatched lengths are
        // a caller bug; fall back to the null-origin path so behavior stays
        // predictable.
        if (candidateOrigin is not null && candidateOrigin.Count != candidates.Count)
            candidateOrigin = null;

        // Validate inference results vector length — must match candidates count.
        if (inferenceResults is not null && inferenceResults.Count != candidates.Count)
            inferenceResults = null;

        // Step 1: Filter to applicable candidates — parameter count must match
        // and each argument type must be assignable to the corresponding parameter type.
        // Keep the candidate's index in the original list so that, if we need
        // the origin-based tiebreaker later, we can look up its origin value.
        var applicable = new List<(ISymbol Symbol, IReadOnlyList<IType> ParamTypes, int OriginalIndex)>();

        for (var c = 0; c < candidates.Count; c++)
        {
            var candidate = candidates[c];

            // Inference-aware filtering: when inference results are provided,
            // exclude candidates where inference was attempted but failed.
            // A null entry means the candidate is non-generic (no inference needed)
            // and passes through unchanged.
            if (inferenceResults is not null && inferenceResults[c] is { } ir && !ir.Success)
                continue;
            var paramTypes = ParameterSignature.Extract(candidate, model);

            if (paramTypes.Count != argumentTypes.Count)
                continue;

            // When a type substitution context is provided (e.g., for
            // constructors of ConstructedType like Optional<int>),
            // substitute type parameters in the extracted parameter types
            // before the applicability check. This maps T → Int32 so that
            // Optional(T value) becomes Optional(Int32 value).
            if (typeSubstitution is var (typeParams, typeArgs))
            {
                var substituted = new IType[paramTypes.Count];
                for (var i = 0; i < paramTypes.Count; i++)
                    substituted[i] = TypeCheckerAnalyzer.SubstituteType(
                        paramTypes[i], typeParams, typeArgs);
                paramTypes = substituted;
            }

            // When inference succeeded for this candidate, substitute the inferred
            // type arguments into the parameter types before the assignability check.
            // This maps T → Int32 so that Process<T>(T value) becomes Process(Int32 value)
            // when inference determined T = Int32.
            if (inferenceResults is not null && inferenceResults[c] is { Success: true } successfulInference
                && candidate is Symbol candidateSymForSubst && candidateSymForSubst.TypeParameters.Count > 0)
            {
                var substituted = new IType[paramTypes.Count];
                for (var i = 0; i < paramTypes.Count; i++)
                    substituted[i] = TypeCheckerAnalyzer.SubstituteType(
                        paramTypes[i], candidateSymForSubst.TypeParameters, successfulInference.InferredTypeArguments!);
                paramTypes = substituted;
            }

            // RefKind filtering: if argument ref kinds are provided, a candidate
            // is only applicable if each parameter's RefKind matches the corresponding
            // argument's RefKind. A ref parameter requires a ref argument and vice versa
            // (Requirement 7.4).
            if (argumentRefKinds is not null)
            {
                var paramRefKinds = ParameterSignature.ExtractRefKinds(candidate, model);
                var refKindMismatch = false;
                for (var i = 0; i < argumentTypes.Count; i++)
                {
                    var paramRefKind = i < paramRefKinds.Count ? paramRefKinds[i] : RefKind.None;
                    var argRefKind = i < argumentRefKinds.Count ? argumentRefKinds[i] : RefKind.None;
                    if (paramRefKind != argRefKind)
                    {
                        refKindMismatch = true;
                        break;
                    }
                }
                if (refKindMismatch)
                    continue;
            }

            var allAssignable = true;
            for (var i = 0; i < argumentTypes.Count; i++)
            {
                // For ref parameters, require exact type match (no implicit conversions).
                // C# §12.6.4.2: For ref/out arguments, the parameter type must match
                // the argument type exactly (identity conversion only).
                var isRefArg = argumentRefKinds is not null
                    && i < argumentRefKinds.Count
                    && argumentRefKinds[i] != RefKind.None;

                if (isRefArg)
                {
                    if (!TypeCheckerAnalyzer.TypesEqual(argumentTypes[i], paramTypes[i]))
                    {
                        allAssignable = false;
                        break;
                    }
                }
                else if (!TypeCheckerAnalyzer.IsAssignable(argumentTypes[i], paramTypes[i]))
                {
                    allAssignable = false;
                    break;
                }
            }

            if (allAssignable)
                applicable.Add((candidate, paramTypes, c));
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
                    return MakeAmbiguousOrTiebreak(applicable, candidateOrigin);
                }
            }

            return new OverloadResolutionResult(
                OverloadResolutionStatus.Success,
                BestCandidate: best.Symbol,
                AmbiguousCandidates: null);
        }

        // No single most-specific candidate — either ambiguous or resolved by
        // the origin-based tiebreaker.
        return MakeAmbiguousOrTiebreak(applicable, candidateOrigin);
    }

    /// <summary>
    /// Applies tiebreakers to a set of otherwise-ambiguous applicable candidates.
    ///
    /// Tiebreaker 1 (non-generic preference): If exactly one candidate is
    /// non-generic while all others are generic, the non-generic candidate wins.
    /// This mirrors C# §12.6.4.3 "better function member" — a non-generic method
    /// is preferred over a generic method with identical parameter types after
    /// type argument substitution.
    ///
    /// Tiebreaker 2 (left-operand precedence, design § 5): When
    /// <paramref name="candidateOrigin"/> is provided, the applicable
    /// candidates are partitioned by their origin value (looked up using the
    /// original candidate-list index preserved in
    /// <c>applicable[i].OriginalIndex</c>). If exactly one candidate has the
    /// minimum origin, it wins and the call resolves to
    /// <see cref="OverloadResolutionStatus.Success"/>. If two or more
    /// candidates share the minimum origin, the tiebreaker cannot
    /// disambiguate and the call remains <see cref="OverloadResolutionStatus.AmbiguousCall"/>.
    ///
    /// When neither tiebreaker resolves the ambiguity, the result is
    /// <see cref="OverloadResolutionStatus.AmbiguousCall"/>.
    /// </summary>
    private static OverloadResolutionResult MakeAmbiguousOrTiebreak(
        List<(ISymbol Symbol, IReadOnlyList<IType> ParamTypes, int OriginalIndex)> applicable,
        IReadOnlyList<int>? candidateOrigin)
    {
        // Tiebreaker 1: Prefer non-generic over generic (C# §12.6.4.3).
        // When exactly one applicable candidate is non-generic and the rest are
        // generic, the non-generic candidate is the better function member.
        var nonGenericCandidates = applicable
            .Where(a => a.Symbol is Symbol s && s.TypeParameters.Count == 0)
            .ToList();

        if (nonGenericCandidates.Count == 1 && applicable.Count > 1)
        {
            return new OverloadResolutionResult(
                OverloadResolutionStatus.Success,
                BestCandidate: nonGenericCandidates[0].Symbol,
                AmbiguousCandidates: null);
        }
        if (candidateOrigin is null)
        {
            return new OverloadResolutionResult(
                OverloadResolutionStatus.AmbiguousCall,
                BestCandidate: null,
                AmbiguousCandidates: applicable.Select(a => a.Symbol).ToList());
        }

        // Find the minimum origin value across the applicable candidates.
        var minOrigin = int.MaxValue;
        foreach (var entry in applicable)
        {
            var origin = candidateOrigin[entry.OriginalIndex];
            if (origin < minOrigin)
                minOrigin = origin;
        }

        // Collect all candidates whose origin equals the minimum. If exactly
        // one such candidate exists, it is the winner; otherwise ambiguity
        // persists (the tiebreaker cannot disambiguate same-origin candidates).
        ISymbol? sole = null;
        var soleCount = 0;
        foreach (var entry in applicable)
        {
            if (candidateOrigin[entry.OriginalIndex] != minOrigin)
                continue;

            sole = entry.Symbol;
            soleCount++;
            if (soleCount > 1)
                break;
        }

        if (soleCount == 1 && sole is not null)
        {
            return new OverloadResolutionResult(
                OverloadResolutionStatus.Success,
                BestCandidate: sole,
                AmbiguousCandidates: null);
        }

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
