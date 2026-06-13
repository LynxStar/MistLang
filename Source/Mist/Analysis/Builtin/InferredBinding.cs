using Mist.Semantics.Contracts;
using Mist.Semantics.Model;

namespace Mist.Analysis.Builtin;

/// <summary>
/// The result of ownership inference for a single local binding.
/// Used by <see cref="OwnershipInferencePass"/> to communicate inferred tiers
/// for <c>var</c> declarations to the flow-sensitive analysis.
/// </summary>
public readonly record struct InferredBinding(
    ISymbol Symbol,
    OwnershipTier InferredTier,
    InferenceReason Reason);

/// <summary>
/// Why a particular tier was chosen during inference.
/// </summary>
public enum InferenceReason
{
    /// <summary>Constructor or owned-returning init — starts as owned candidate.</summary>
    OwnedInit,

    /// <summary>Used as receiver for a method requiring a specific this-tier.</summary>
    ThisTierRequired,

    /// <summary>Passed to an owned parameter — must be owned (consumed).</summary>
    OwnedParamConsumption,

    /// <summary>No ownership usage detected — shared fallback.</summary>
    SharedFallback,

    /// <summary>Inference conflict resolved by downgrade.</summary>
    Downgraded,
}
