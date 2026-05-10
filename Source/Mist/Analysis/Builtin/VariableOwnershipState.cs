namespace Mist.Analysis.Builtin;

/// <summary>
/// Tracks the current ownership state of a variable during flow-sensitive analysis.
/// The state changes as the variable is moved, lent, or restored.
/// </summary>
public enum VariableOwnershipState
{
    /// <summary>Variable is live and accessible.</summary>
    Live,

    /// <summary>Variable has been moved; access is an error.</summary>
    Moved,

    /// <summary>Variable is lent as ref; owner has no access.</summary>
    LentRef,

    /// <summary>Variable is lent as frozen ref; owner cannot mutate.</summary>
    LentFrozen,

    /// <summary>Variable is lent as readonly ref; owner retains full access.</summary>
    LentReadonly,
}
