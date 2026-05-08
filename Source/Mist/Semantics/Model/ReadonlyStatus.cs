namespace Mist.Semantics.Model;

/// <summary>
/// Tracks the readonly classification of a symbol.
/// </summary>
public enum ReadonlyStatus
{
    /// <summary>Not yet determined (pre-inference).</summary>
    Unknown,

    /// <summary>Explicitly marked readonly by the developer.</summary>
    ExplicitReadonly,

    /// <summary>Inferred as readonly by the ReadonlyInferenceEngine.</summary>
    InferredReadonly,

    /// <summary>Implicitly readonly because the containing type is readonly.</summary>
    ImplicitReadonly,

    /// <summary>Not readonly (mutable).</summary>
    Mutable,
}
