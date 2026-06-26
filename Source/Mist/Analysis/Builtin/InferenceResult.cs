using Mist.Semantics.Contracts;

namespace Mist.Analysis.Builtin;

/// <summary>
/// The result of type argument inference for a single generic method call site.
/// Contains either the successfully inferred type arguments or details about why
/// inference failed (uninferrable type parameters or conflicting bindings).
/// </summary>
/// <param name="Success">Whether all type parameters were successfully inferred.</param>
/// <param name="InferredTypeArguments">
/// The inferred concrete types in declaration order when inference succeeds; null on failure.
/// </param>
/// <param name="UninferrableTypeParameters">
/// Names of type parameters that could not be inferred (not present in any parameter position);
/// null when inference succeeds.
/// </param>
/// <param name="Conflicts">
/// Type parameters that received conflicting bindings from different argument positions;
/// null when inference succeeds.
/// </param>
public readonly record struct InferenceResult(
    bool Success,
    IReadOnlyList<IType>? InferredTypeArguments,
    IReadOnlyList<string>? UninferrableTypeParameters,
    IReadOnlyList<(string TypeParameter, IType First, IType Second)>? Conflicts);